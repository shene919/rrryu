﻿using System.Diagnostics.CodeAnalysis;

namespace Ryujinx.HLE.HOS.Services.Mii.Types
{
    [SuppressMessage("Design", "CA1069: Enums values should not be duplicated")]
    enum FacelineColor : byte
    {
        Beige,
        WarmBeige,
        Natural,
        Honey,
        Chestnut,
        Porcelain,
        Ivory,
        WarmIvory,
        Almond,
        Espresso,

        Min = 0,
        Max = 9
    }
}