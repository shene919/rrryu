using Ryujinx.Common;
using Ryujinx.Common.Logging;
using Ryujinx.Common.Memory;
using Ryujinx.Graphics.GAL;
using Ryujinx.Graphics.Texture;
using Ryujinx.Graphics.Texture.Astc;
using Ryujinx.Graphics.Texture.FileFormats;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Ryujinx.Graphics.Gpu.Image
{
    public class DiskTextureStorage
    {
        private const long MinTimeDeltaForRealTimeLoad = 5; // Seconds.

        private enum FileFormat
        {
            Dds,
            Png
        }

        private struct ScopedLoadLock : IDisposable
        {
            private readonly DiskTextureStorage _storage;
            private readonly string _outputFileName;
            private bool _isNewDump;

            public ScopedLoadLock(DiskTextureStorage storage, string outputFileName)
            {
                _storage = storage;
                _outputFileName = outputFileName;
                _isNewDump = storage._newDumpFiles.TryAdd(outputFileName, long.MaxValue);
            }

            public void Cancel()
            {
                _isNewDump = false;
                _storage._newDumpFiles.TryRemove(_outputFileName, out _);
            }

            public readonly void Dispose()
            {
                if (_isNewDump && TryGetFileTimestamp(_outputFileName, out long timestamp))
                {
                    _storage._newDumpFiles[_outputFileName] = timestamp;
                }
            }
        }

        private readonly struct TextureRequest
        {
            public readonly int Width;
            public readonly int Height;
            public readonly int Depth;
            public readonly int Layers;
            public readonly int Levels;
            public readonly Format Format;
            public readonly Target Target;
            public readonly byte[] Data;

            public TextureRequest(int width, int height, int depth, int layers, int levels, Format format,
                Target target, byte[] data)
            {
                Width = width;
                Height = height;
                Depth = depth;
                Layers = layers;
                Levels = levels;
                Format = format;
                Target = target;
                Data = data;
            }
        }

        private AsyncWorkQueue<(Texture, TextureRequest)> _exportQueue;
        private readonly List<string> _importList;

        private readonly ConcurrentDictionary<string, long> _newDumpFiles;
        private readonly ConcurrentDictionary<string, Texture> _fileToTextureMap;
        private FileSystemWatcher _fileSystemWatcher;

        private string _outputDirectoryPath;
        private FileFormat _outputFormat;
        private bool _enableTextureDump;
        private bool _enableRealTimeTextureEdit;

        internal bool IsActive => !string.IsNullOrEmpty(_outputDirectoryPath) || _importList.Count != 0;

        internal DiskTextureStorage()
        {
            _importList = new List<string>();

            _newDumpFiles = new ConcurrentDictionary<string, long>();
            _fileToTextureMap = new ConcurrentDictionary<string, Texture>();
        }

        internal void Initialize()
        {
            _enableTextureDump = GraphicsConfig.EnableTextureDump;
            _enableRealTimeTextureEdit = GraphicsConfig.EnableTextureRealTimeEdit;

            if (_enableRealTimeTextureEdit)
            {
                _fileSystemWatcher = new FileSystemWatcher();
                _fileSystemWatcher.Changed += OnChanged;
            }

            SetOutputDirectory(GraphicsConfig.TextureDumpPath);
            _outputFormat = GraphicsConfig.TextureDumpFormatPng ? FileFormat.Png : FileFormat.Dds;

            if (_enableTextureDump)
            {
                _exportQueue = new AsyncWorkQueue<(Texture, TextureRequest)>(ExportTexture, "GPU.TextureExportQueue");
            }
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType != WatcherChangeTypes.Changed)
            {
                return;
            }

            // If this a new file that we just created, ignore it.
            if (_newDumpFiles.TryGetValue(e.FullPath, out long savedTimestamp) &&
                TryGetFileTimestamp(e.FullPath, out long currentTimestamp) &&
                savedTimestamp > currentTimestamp - MinTimeDeltaForRealTimeLoad)
            {
                return;
            }

            for (int attempt = 0; attempt < 100; attempt++)
            {
                try
                {
                    File.ReadAllBytes(e.FullPath);
                    break;
                }
                catch (Exception)
                {
                    Thread.Sleep(10);
                }
            }

            if (_fileToTextureMap.TryGetValue(e.Name, out Texture texture))
            {
                texture.ForceReimport();
            }
        }

        public void AddInputDirectory(string directoryPath)
        {
            if (Directory.Exists(directoryPath) && !_importList.Contains(directoryPath))
            {
                _importList.Add(directoryPath);
            }
        }

        public void SetOutputDirectory(string directoryPath)
        {
            string previousOutputDirectoryPath = _outputDirectoryPath;

            if (!string.IsNullOrEmpty(previousOutputDirectoryPath))
            {
                _importList.Remove(previousOutputDirectoryPath);
            }

            if (Directory.Exists(directoryPath))
            {
                _outputDirectoryPath = directoryPath;
                AddInputDirectory(directoryPath);

                if (_enableRealTimeTextureEdit)
                {
                    _fileSystemWatcher.Path = directoryPath;
                    _fileSystemWatcher.EnableRaisingEvents = true;
                }
            }
            else
            {
                _outputDirectoryPath = null;
            }
        }

        internal TextureInfoOverride? ImportTexture(out MemoryOwner<byte> cachedData, Texture texture, byte[] data)
        {
            cachedData = default;

            if (!IsSupportedFormat(texture.Format))
            {
                return null;
            }

            TextureInfoOverride? infoOverride = ImportDdsTexture(out cachedData, texture, data);

            if (!infoOverride.HasValue)
            {
                infoOverride = ImportPngTexture(out cachedData, texture, data);
            }

            return infoOverride;
        }


        private TextureInfoOverride? ImportDdsTexture(out MemoryOwner<byte> cachedData, Texture texture, byte[] data)
        {
            cachedData = default;

            if (!IsSupportedFormat(texture.Format))
            {
                return null;
            }

            TextureRequest request = new(
                texture.Width,
                texture.Height,
                texture.Depth,
                texture.Layers,
                texture.Info.Levels,
                texture.Format,
                texture.Target,
                data);

            ImageParameters parameters = default;
            MemoryOwner<byte> buffer = null;

            bool imported = false;
            string fileName = BuildFileName(request, "dds");

            foreach (string inputDirectoryPath in _importList)
            {
                string inputFileName = Path.Combine(inputDirectoryPath, fileName);

                if (File.Exists(inputFileName))
                {
                    _fileToTextureMap.AddOrUpdate(fileName, texture, (key, old) => texture);

                    byte[] imageFile = null;

                    try
                    {
                        imageFile = File.ReadAllBytes(inputFileName);
                    }
                    catch (IOException ex)
                    {
                        LogReadException(ex, inputFileName);
                        break;
                    }

                    ImageLoadResult loadResult = DdsFileFormat.TryLoadHeader(imageFile, out parameters);

                    if (loadResult != ImageLoadResult.Success)
                    {
                        LogFailureResult(loadResult, inputFileName);
                        break;
                    }

                    buffer = MemoryOwner<byte>.Rent(DdsFileFormat.CalculateSize(parameters));

                    loadResult = DdsFileFormat.TryLoadData(imageFile, buffer.Span);

                    if (loadResult != ImageLoadResult.Success)
                    {
                        LogFailureResult(loadResult, inputFileName);
                        break;
                    }

                    imported = true;
                    break;
                }
            }

            if (!imported)
            {
                return null;
            }

            if (parameters.Format == ImageFormat.B8G8R8A8Unorn)
            {
                for (int i = 0; i < buffer.Length; i += 4)
                {
                    (buffer.Span[i + 2], buffer.Span[i]) = (buffer.Span[i], buffer.Span[i + 2]);
                }
            }

            cachedData = buffer;

            return new TextureInfoOverride(
                parameters.Width,
                parameters.Height,
                parameters.DepthOrLayers,
                parameters.Levels,
                ConvertToFormat(parameters.Format, IsSupportedSrgbFormat(request.Format)));
        }

        private TextureInfoOverride? ImportPngTexture(out MemoryOwner<byte> cachedData, Texture texture, byte[] data)
        {
            cachedData = default;

            if (!IsSupportedFormat(texture.Format))
            {
                return null;
            }

            TextureRequest request = new(
                texture.Width,
                texture.Height,
                texture.Depth,
                texture.Layers,
                texture.Info.Levels,
                texture.Format,
                texture.Target,
                data);

            MemoryOwner<byte> buffer = null;

            int importedFirstLevel = 0;
            int importedWidth = 0;
            int importedHeight = 0;
            int levels = 0;
            int slices = 0;
            int writtenSize = 0;
            int offset = 0;

            DoForEachSlice(request, (level, slice, _, _) =>
            {
                int sliceSize = (importedWidth | importedHeight) != 0
                    ? Math.Max(1, importedWidth >> level) * Math.Max(1, importedHeight >> level) * 4
                    : 0;

                bool imported = false;
                string fileName = BuildFileName(request, level, slice, "png");

                foreach (string inputDirectoryPath in _importList)
                {
                    string inputFileName = Path.Combine(inputDirectoryPath, fileName);

                    if (File.Exists(inputFileName))
                    {
                        _fileToTextureMap.AddOrUpdate(fileName, texture, (key, old) => texture);

                        byte[] imageFile = null;

                        try
                        {
                            imageFile = File.ReadAllBytes(inputFileName);
                        }
                        catch (IOException ex)
                        {
                            LogReadException(ex, inputFileName);
                            break;
                        }

                        ImageLoadResult loadResult =
                            PngFileFormat.TryLoadHeader(imageFile, out ImageParameters parameters);

                        if (loadResult != ImageLoadResult.Success)
                        {
                            LogFailureResult(loadResult, inputFileName);
                            break;
                        }

                        int importedSizeWL = Math.Max(1, importedWidth >> level);
                        int importedSizeHL = Math.Max(1, importedHeight >> level);

                        if (writtenSize == 0 ||
                            (importedSizeWL == parameters.Width && importedSizeHL == parameters.Height))
                        {
                            if (writtenSize == 0)
                            {
                                importedFirstLevel = level;
                                importedWidth = parameters.Width << level;
                                importedHeight = parameters.Height << level;
                                sliceSize = Math.Max(1, importedWidth >> level) * Math.Max(1, importedHeight >> level) *
                                            4;
                                buffer = MemoryOwner<byte>.Rent(CalculateSize(importedWidth, importedHeight,
                                    request.Depth, request.Layers, request.Levels));
                            }

                            loadResult = PngFileFormat.TryLoadData(imageFile, buffer.Span.Slice(offset, sliceSize));

                            if (loadResult != ImageLoadResult.Success)
                            {
                                LogFailureResult(loadResult, inputFileName);
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }

                        imported = true;
                        break;
                    }
                }

                if (imported)
                {
                    levels = level + 1;

                    if (level == importedFirstLevel)
                    {
                        slices = slice + 1;
                    }

                    writtenSize = offset + sliceSize;
                }

                offset += sliceSize;

                return imported;
            });

            if (writtenSize == 0)
            {
                return null;
            }

            if (writtenSize == buffer.Memory.Length)
            {
                cachedData = buffer;
            }
            else
            {
                cachedData = MemoryOwner<byte>.RentCopy(buffer.Span[..writtenSize]);
            }

            return new TextureInfoOverride(
                importedWidth,
                importedHeight,
                slices,
                levels,
                new FormatInfo(IsSupportedSrgbFormat(request.Format) ? Format.R8G8B8A8Srgb : Format.R8G8B8A8Unorm, 1, 1,
                    4, 4));
        }

        private static int CalculateSize(int width, int height, int depth, int layers, int levels)
        {
            int size = 0;

            for (int level = 0; level < levels; level++)
            {
                int w = Math.Max(1, width >> level);
                int h = Math.Max(1, height >> level);
                int d = Math.Max(1, depth >> level);
                int sliceSize = w * h * 4;

                size += sliceSize * layers * d;
            }

            return size;
        }

        internal void EnqueueTextureDataForExport(Texture texture, byte[] data)
        {
            if (_enableTextureDump && !string.IsNullOrEmpty(_outputDirectoryPath))
            {
                _exportQueue.Add((texture, new(
                    texture.Width,
                    texture.Height,
                    texture.Depth,
                    texture.Layers,
                    texture.Info.Levels,
                    texture.Format,
                    texture.Target,
                    data)));
            }
        }

        private void ExportTexture((Texture, TextureRequest) tuple)
        {
            if (_outputFormat == FileFormat.Png)
            {
                ExportPngTexturePerSlice(tuple.Item1, tuple.Item2);
            }
            else
            {
                ExportDdsTexture(tuple.Item1, tuple.Item2);
            }
        }

        private void ExportDdsTexture(Texture texture, TextureRequest request)
        {
            if (!TryGetDimensions(request.Target, out ImageDimensions imageDimensions))
            {
                return;
            }

            byte[] data = request.Data;

            if (!TryGetFormat(request.Format, out ImageFormat imageFormat))
            {
                data = ConvertFormatToRgba8(request);
                imageFormat = ImageFormat.R8G8B8A8Unorm;
            }

            if (data == null)
            {
                return;
            }

            string fileName = BuildFileName(request, "dds");
            string outputFileName = Path.Combine(_outputDirectoryPath, fileName);

            using ScopedLoadLock loadLock = new(this, outputFileName);

            _fileToTextureMap.TryAdd(fileName, texture);

            ImageParameters parameters = new(
                request.Width,
                request.Height,
                request.Depth * request.Layers,
                request.Levels,
                imageFormat,
                imageDimensions);

            try
            {
                using FileStream fs = new(outputFileName, FileMode.Create);
                DdsFileFormat.Save(fs, parameters, data);
            }
            catch (IOException ex)
            {
                LogWriteException(ex, outputFileName);
                loadLock.Cancel();
            }
        }

        private void ExportPngTexturePerSlice(Texture texture, TextureRequest request)
        {
            byte[] data = ConvertFormatToRgba8(request);

            if (data == null)
            {
                return;
            }

            DoForEachSlice(request, (level, slice, offset, sliceSize) =>
            {
                ReadOnlySpan<byte> buffer = data.AsSpan();

                int w = Math.Max(1, request.Width >> level);
                int h = Math.Max(1, request.Height >> level);

                string fileName = BuildFileName(request, level, slice, "png");
                string outputFileName = Path.Combine(_outputDirectoryPath, fileName);

                using ScopedLoadLock loadLock = new(this, outputFileName);

                _fileToTextureMap.TryAdd(fileName, texture);

                ImageParameters parameters = new(w, h, 1, 1, ImageFormat.R8G8B8A8Unorm, ImageDimensions.Dim2D);

                try
                {
                    using FileStream fs = new(outputFileName, FileMode.Create);
                    PngFileFormat.Save(fs, parameters, buffer.Slice(offset, sliceSize), fastMode: true);
                }
                catch (IOException ex)
                {
                    LogWriteException(ex, outputFileName);
                    loadLock.Cancel();
                    return false;
                }

                return true;
            });
        }

        private static bool TryGetFileTimestamp(string fileName, out long timestamp)
        {
            try
            {
                DateTime time = File.GetLastWriteTimeUtc(fileName);
                timestamp = ((DateTimeOffset)time).ToUnixTimeSeconds();
                return true;
            }
            catch (Exception)
            {
                timestamp = 0;
                return false;
            }
        }

        private static void DoForEachSlice(in TextureRequest request, Func<int, int, int, int, bool> callback)
        {
            bool is3D = request.Depth > 1;
            int offset = 0;

            for (int level = 0; level < request.Levels; level++)
            {
                int w = Math.Max(1, request.Width >> level);
                int h = Math.Max(1, request.Height >> level);
                int d = is3D ? Math.Max(1, request.Depth >> level) : request.Layers;
                int sliceSize = w * h * 4;

                for (int slice = 0; slice < d; slice++)
                {
                    if (!callback(level, slice, offset, sliceSize))
                    {
                        break;
                    }

                    offset += sliceSize;
                }
            }
        }

        private static string BuildFileName(TextureRequest request, string extension)
        {
            int w = request.Width;
            int h = request.Height;
            int d = request.Depth * request.Layers;
            string hash = ComputeHash(request.Data);
            return $"{GetNamePrefix(request.Target)}_{hash}_{w}x{h}x{d}.{extension}";
        }

        private static string BuildFileName(TextureRequest request, int level, int slice, string extension)
        {
            int w = request.Width;
            int h = request.Height;
            int d = request.Depth * request.Layers;
            string hash = ComputeHash(request.Data);
            return $"{GetNamePrefix(request.Target)}_{hash}_{w}x{h}x{d}_{level}x{slice}.{extension}";
        }

        private static string GetNamePrefix(Target target)
        {
            return target switch
            {
                Target.Texture2D => "tex2d",
                Target.Texture2DArray => "texa2d",
                Target.Texture3D => "tex3d",
                Target.Cubemap => "texcube",
                Target.CubemapArray => "texacube",
                _ => "tex",
            };
        }

        private static string ComputeHash(byte[] data)
        {
            Hash128 hash = XXHash128.ComputeHash(data);
            return $"{hash.High:x16}{hash.Low:x16}";
        }

        private static bool TryGetFormat(Format format, out ImageFormat imageFormat)
        {
            switch (format)
            {
                case Format.Bc1RgbaSrgb:
                case Format.Bc1RgbaUnorm:
                    imageFormat = ImageFormat.Bc1RgbaUnorm;
                    return true;
                case Format.Bc2Srgb:
                case Format.Bc2Unorm:
                    imageFormat = ImageFormat.Bc2Unorm;
                    return true;
                case Format.Bc3Srgb:
                case Format.Bc3Unorm:
                    imageFormat = ImageFormat.Bc3Unorm;
                    return true;
                case Format.R8G8B8A8Srgb:
                case Format.R8G8B8A8Unorm:
                    imageFormat = ImageFormat.R8G8B8A8Unorm;
                    return true;
                case Format.R5G6B5Unorm:
                    imageFormat = ImageFormat.R5G6B5Unorm;
                    return true;
                case Format.R5G5B5A1Unorm:
                    imageFormat = ImageFormat.R5G5B5A1Unorm;
                    return true;
                case Format.R4G4B4A4Unorm:
                    imageFormat = ImageFormat.R4G4B4A4Unorm;
                    return true;
            }

            imageFormat = default;
            return false;
        }

        private static bool TryGetDimensions(Target target, out ImageDimensions imageDimensions)
        {
            switch (target)
            {
                case Target.Texture2D:
                    imageDimensions = ImageDimensions.Dim2D;
                    return true;
                case Target.Texture2DArray:
                    imageDimensions = ImageDimensions.Dim2DArray;
                    return true;
                case Target.Texture3D:
                    imageDimensions = ImageDimensions.Dim3D;
                    return true;
                case Target.Cubemap:
                    imageDimensions = ImageDimensions.DimCube;
                    return true;
                case Target.CubemapArray:
                    imageDimensions = ImageDimensions.DimCubeArray;
                    return true;
            }

            imageDimensions = default;
            return false;
        }

        private static byte[] ConvertFormatToRgba8(in TextureRequest request)
        {
            byte[] data = request.Data;
            int width = request.Width;
            int height = request.Height;
            int depth = request.Depth;
            int layers = request.Layers;
            int levels = request.Levels;

            switch (request.Format)
            {
                case Format.Astc4x4Srgb:
                case Format.Astc4x4Unorm:
                    return DecodeAstc(data, 4, 4, width, height, depth, levels, layers).Memory.ToArray();
                    ;
                case Format.Astc5x4Srgb:
                case Format.Astc5x4Unorm:
                    return DecodeAstc(data, 5, 4, width, height, depth, levels, layers).Memory.ToArray();
                    ;
                case Format.Astc5x5Srgb:
                case Format.Astc5x5Unorm:
                    return DecodeAstc(data, 5, 5, width, height, depth, levels, layers).Memory.ToArray();
                    ;
                case Format.Astc6x5Srgb:
                case Format.Astc6x5Unorm:
                    return DecodeAstc(data, 6, 5, width, height, depth, levels, layers).Memory.ToArray();
                    ;
                case Format.Astc6x6Srgb:
                case Format.Astc6x6Unorm:
                    return DecodeAstc(data, 6, 6, width, height, depth, levels, layers).Memory.ToArray();
                    ;
                case Format.Astc8x5Srgb:
                case Format.Astc8x5Unorm:
                    return DecodeAstc(data, 8, 5, width, height, depth, levels, layers).Memory.ToArray();
                    ;
                case Format.Astc8x6Srgb:
                case Format.Astc8x6Unorm:
                    return DecodeAstc(data, 8, 6, width, height, depth, levels, layers).Memory.ToArray();
                    ;
                case Format.Astc8x8Srgb:
                case Format.Astc8x8Unorm:
                    return DecodeAstc(data, 8, 8, width, height, depth, levels, layers).Memory.ToArray();
                    ;
                case Format.Astc10x5Srgb:
                case Format.Astc10x5Unorm:
                    return DecodeAstc(data, 10, 5, width, height, depth, levels, layers).Memory.ToArray();
                    ;
                case Format.Astc10x6Srgb:
                case Format.Astc10x6Unorm:
                    return DecodeAstc(data, 10, 6, width, height, depth, levels, layers).Memory.ToArray();
                    ;
                case Format.Astc10x8Srgb:
                case Format.Astc10x8Unorm:
                    return DecodeAstc(data, 10, 8, width, height, depth, levels, layers).Memory.ToArray();
                    ;
                case Format.Astc10x10Srgb:
                case Format.Astc10x10Unorm:
                    return DecodeAstc(data, 10, 10, width, height, depth, levels, layers).Memory.ToArray();
                    ;
                case Format.Astc12x10Srgb:
                case Format.Astc12x10Unorm:
                    return DecodeAstc(data, 12, 10, width, height, depth, levels, layers).Memory.ToArray();
                    ;
                case Format.Astc12x12Srgb:
                case Format.Astc12x12Unorm:
                    return DecodeAstc(data, 12, 12, width, height, depth, levels, layers).Memory.ToArray();
                    ;
                case Format.Bc1RgbaSrgb:
                case Format.Bc1RgbaUnorm:
                    return BCnDecoder.DecodeBC1(data, width, height, depth, levels, layers).Memory.ToArray();
                case Format.Bc2Srgb:
                case Format.Bc2Unorm:
                    return BCnDecoder.DecodeBC2(data, width, height, depth, levels, layers).Memory.ToArray();
                case Format.Bc3Srgb:
                case Format.Bc3Unorm:
                    return BCnDecoder.DecodeBC3(data, width, height, depth, levels, layers).Memory.ToArray();
                /*case Format.Bc4Snorm:
                case Format.Bc4Unorm:
                    return BCnDecoder.DecodeBC4(data, width, height, depth, levels, layers, request.Format == Format.Bc4Snorm);
                case Format.Bc5Snorm:
                case Format.Bc5Unorm:
                    return BCnDecoder.DecodeBC5(data, width, height, depth, levels, layers, request.Format == Format.Bc5Snorm);
                case Format.Bc6HSfloat:
                case Format.Bc6HUfloat:
                    return BCnDecoder.DecodeBC6(data, width, height, depth, levels, layers, request.Format == Format.Bc6HSfloat);*/
                case Format.Bc7Srgb:
                case Format.Bc7Unorm:
                    return BCnDecoder.DecodeBC7(data, width, height, depth, levels, layers).Memory.ToArray();
                case Format.Etc2RgbaSrgb:
                case Format.Etc2RgbaUnorm:
                    return ETC2Decoder.DecodeRgba(data, width, height, depth, levels, layers).Memory.ToArray();
                case Format.Etc2RgbSrgb:
                case Format.Etc2RgbUnorm:
                    return ETC2Decoder.DecodeRgb(data, width, height, depth, levels, layers).Memory.ToArray();
                case Format.Etc2RgbPtaSrgb:
                case Format.Etc2RgbPtaUnorm:
                    return ETC2Decoder.DecodePta(data, width, height, depth, levels, layers).Memory.ToArray();
                case Format.R8G8B8A8Unorm:
                case Format.R8G8B8A8Srgb:
                    return request.Data;
                case Format.B5G6R5Unorm:
                case Format.R5G6B5Unorm:
                    return PixelConverter.ConvertR5G6B5ToR8G8B8A8(data, width).Memory.ToArray();
                case Format.B5G5R5A1Unorm:
                case Format.R5G5B5X1Unorm:
                case Format.R5G5B5A1Unorm:
                    return PixelConverter.ConvertR5G5B5ToR8G8B8A8(data, width, request.Format == Format.R5G5B5X1Unorm).Memory.ToArray();
                case Format.A1B5G5R5Unorm:
                    return PixelConverter.ConvertA1B5G5R5ToR8G8B8A8(data, width).Memory.ToArray();
                case Format.R4G4B4A4Unorm:
                    return PixelConverter.ConvertR4G4B4A4ToR8G8B8A8(data, width).Memory.ToArray();
            }

            return null;
        }

        private static MemoryOwner<byte> DecodeAstc(byte[] data, int blockWidth, int blockHeight, int width, int height, int depth,
            int levels, int layers)
        {
            if (!AstcDecoder.TryDecodeToRgba8P(
                    data,
                    blockWidth,
                    blockHeight,
                    width,
                    height,
                    depth,
                    levels,
                    layers,
                    out MemoryOwner<byte> decoded))
            {
                decoded = MemoryOwner<byte>.Rent(width * height * depth * layers * 4);
            }

            return decoded;
        }

        private static bool IsSupportedFormat(Format format)
        {
            switch (format)
            {
                case Format.Astc4x4Srgb:
                case Format.Astc4x4Unorm:
                case Format.Astc5x4Srgb:
                case Format.Astc5x4Unorm:
                case Format.Astc5x5Srgb:
                case Format.Astc5x5Unorm:
                case Format.Astc6x5Srgb:
                case Format.Astc6x5Unorm:
                case Format.Astc6x6Srgb:
                case Format.Astc6x6Unorm:
                case Format.Astc8x5Srgb:
                case Format.Astc8x5Unorm:
                case Format.Astc8x6Srgb:
                case Format.Astc8x6Unorm:
                case Format.Astc8x8Srgb:
                case Format.Astc8x8Unorm:
                case Format.Astc10x5Srgb:
                case Format.Astc10x5Unorm:
                case Format.Astc10x6Srgb:
                case Format.Astc10x6Unorm:
                case Format.Astc10x8Srgb:
                case Format.Astc10x8Unorm:
                case Format.Astc10x10Srgb:
                case Format.Astc10x10Unorm:
                case Format.Astc12x10Srgb:
                case Format.Astc12x10Unorm:
                case Format.Astc12x12Srgb:
                case Format.Astc12x12Unorm:
                case Format.Bc1RgbaSrgb:
                case Format.Bc1RgbaUnorm:
                case Format.Bc2Srgb:
                case Format.Bc2Unorm:
                case Format.Bc3Srgb:
                case Format.Bc3Unorm:
                case Format.Bc7Srgb:
                case Format.Bc7Unorm:
                case Format.Etc2RgbaSrgb:
                case Format.Etc2RgbaUnorm:
                case Format.Etc2RgbSrgb:
                case Format.Etc2RgbUnorm:
                case Format.Etc2RgbPtaSrgb:
                case Format.Etc2RgbPtaUnorm:
                case Format.R8G8B8A8Unorm:
                case Format.R8G8B8A8Srgb:
                case Format.B5G6R5Unorm:
                case Format.R5G6B5Unorm:
                case Format.B5G5R5A1Unorm:
                case Format.R5G5B5X1Unorm:
                case Format.R5G5B5A1Unorm:
                case Format.A1B5G5R5Unorm:
                case Format.R4G4B4A4Unorm:
                    return true;
            }

            return false;
        }

        private static bool IsSupportedSrgbFormat(Format format)
        {
            switch (format)
            {
                case Format.Astc4x4Srgb:
                case Format.Astc5x4Srgb:
                case Format.Astc5x5Srgb:
                case Format.Astc6x5Srgb:
                case Format.Astc6x6Srgb:
                case Format.Astc8x5Srgb:
                case Format.Astc8x6Srgb:
                case Format.Astc8x8Srgb:
                case Format.Astc10x5Srgb:
                case Format.Astc10x6Srgb:
                case Format.Astc10x8Srgb:
                case Format.Astc10x10Srgb:
                case Format.Astc12x10Srgb:
                case Format.Astc12x12Srgb:
                case Format.Bc1RgbaSrgb:
                case Format.Bc2Srgb:
                case Format.Bc3Srgb:
                case Format.Bc7Srgb:
                case Format.Etc2RgbaSrgb:
                case Format.Etc2RgbSrgb:
                case Format.Etc2RgbPtaSrgb:
                case Format.R8G8B8A8Srgb:
                    return true;
            }

            return false;
        }

        private static FormatInfo ConvertToFormat(ImageFormat format, bool isSrgb)
        {
            return format switch
            {
                ImageFormat.Bc1RgbaUnorm => new FormatInfo(isSrgb ? Format.Bc1RgbaSrgb : Format.Bc1RgbaUnorm, 4, 4, 8,
                    4),
                ImageFormat.Bc2Unorm => new FormatInfo(isSrgb ? Format.Bc2Srgb : Format.Bc2Unorm, 4, 4, 16, 4),
                ImageFormat.Bc3Unorm => new FormatInfo(isSrgb ? Format.Bc3Srgb : Format.Bc3Unorm, 4, 4, 16, 4),
                ImageFormat.R8G8B8A8Unorm or ImageFormat.B8G8R8A8Unorn => new FormatInfo(
                    isSrgb ? Format.R8G8B8A8Srgb : Format.R8G8B8A8Unorm, 1, 1, 4, 4),
                ImageFormat.R5G6B5Unorm => new FormatInfo(Format.R5G6B5Unorm, 1, 1, 2, 3),
                ImageFormat.R5G5B5A1Unorm => new FormatInfo(Format.R5G5B5A1Unorm, 1, 1, 2, 4),
                ImageFormat.R4G4B4A4Unorm => new FormatInfo(Format.R4G4B4A4Unorm, 1, 1, 2, 4),
                _ => throw new ArgumentException($"Invalid format {format}."),
            };
        }

        private static void LogFailureResult(ImageLoadResult result, string fullPath)
        {
            string fileName = Path.GetFileName(fullPath);

            switch (result)
            {
                case ImageLoadResult.CorruptedHeader:
                    Logger.Error?.Print(LogClass.Gpu,
                        $"Failed to load \"{fileName}\" because the file header is corrupted.");
                    break;
                case ImageLoadResult.CorruptedData:
                    Logger.Error?.Print(LogClass.Gpu,
                        $"Failed to load \"{fileName}\" because the file data is corrupted.");
                    break;
                case ImageLoadResult.DataTooShort:
                    Logger.Error?.Print(LogClass.Gpu,
                        $"Failed to load \"{fileName}\" because some data is missing from the file.");
                    break;
                case ImageLoadResult.OutputTooShort:
                    Logger.Error?.Print(LogClass.Gpu,
                        $"Failed to load \"{fileName}\" because the output buffer was not large enough.");
                    break;
                case ImageLoadResult.UnsupportedFormat:
                    Logger.Error?.Print(LogClass.Gpu,
                        $"Failed to load \"{fileName}\" because the image format is not currently supported.");
                    break;
            }
        }

        private static void LogReadException(IOException exception, string fullPath)
        {
            Logger.Error?.Print(LogClass.Gpu, exception.ToString());

            string fileName = Path.GetFileName(fullPath);

            Logger.Error?.Print(LogClass.Gpu, $"Failed to load \"{fileName}\", see logged exception for details.");
        }

        private static void LogWriteException(IOException exception, string fullPath)
        {
            Logger.Error?.Print(LogClass.Gpu, exception.ToString());

            string fileName = Path.GetFileName(fullPath);

            Logger.Error?.Print(LogClass.Gpu, $"Failed to save \"{fileName}\", see logged exception for details.");
        }
    }
}
