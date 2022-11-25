using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using System;
using System.Threading.Tasks;
using Button = Avalonia.Controls.Button;

namespace Ryujinx.Ava.Ui.Controls
{
    public class ContentDialogOverlay : ContentDialog, IStyleable
    {
        Type IStyleable.StyleKey => typeof(ContentDialog);

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
		{
            base.OnApplyTemplate(e);

			_primaryButton = e.NameScope.Get<Button>("PrimaryButton");
            _secondaryButton = e.NameScope.Get<Button>("SecondaryButton");
            _closeButton = e.NameScope.Get<Button>("CloseButton");
        }

        public async Task<ContentDialogResult> ShowAsync(Window overlayWindow, ContentDialogPlacement placement = ContentDialogPlacement.Popup)
		{
			if (placement == ContentDialogPlacement.InPlace)
				throw new NotImplementedException("InPlace not implemented yet");
			tcs = new TaskCompletionSource<ContentDialogResult>();

			OnOpening();

			if (this.Parent != null)
            {
                _originalHost = Parent;
                switch (_originalHost)
                {
                    case Panel p:
                        p.Children.Remove(this);
                        break;
                    case Decorator d:
                        d.Child = null;
                        break;
                    case IContentControl cc:
                        cc.Content = null;
                        break;
                    case IContentPresenter cp:
                        cp.Content = null;
                        break;
                }
            }

			_dialogHost ??= new DialogHost();

			_dialogHost.Content = this;

            var ol = OverlayLayer.GetOverlayLayer(overlayWindow);
            if (ol == null)
                throw new InvalidOperationException();

            ol.Children.Add(_dialogHost);

            IsVisible = true;
			ShowCore();
			SetupDialog();

            return await tcs.Task;
		}

        private void ShowCore()
		{
			IsVisible = true;
			PseudoClasses.Set(":hidden", false);
			PseudoClasses.Set(":open", true);

			OnOpened();
		}

        private void SetupDialog()
		{
			if (_primaryButton == null)
				ApplyTemplate();

			PseudoClasses.Set(":primary", !string.IsNullOrEmpty(PrimaryButtonText));
			PseudoClasses.Set(":secondary", !string.IsNullOrEmpty(SecondaryButtonText));
			PseudoClasses.Set(":close", !string.IsNullOrEmpty(CloseButtonText));

            if (this.FindAncestorOfType<DialogHost>() == null)
            {
                return;
            }

            switch (DefaultButton)
            {
                case ContentDialogButton.Primary:
                    if (!_primaryButton.IsVisible)
                        break;

                    _primaryButton.Classes.Add("accent");
                    _secondaryButton.Classes.Remove("accent");
                    _closeButton.Classes.Remove("accent");
                    if (Content is IControl cp && cp.Focusable)
                    {
                        cp.Focus();
                    }
                    else
                    {
                        _primaryButton.Focus();
                    }

                    break;

                case ContentDialogButton.Secondary:
                    if (!_secondaryButton.IsVisible)
                        break;

                    _secondaryButton.Classes.Add("accent");
                    _primaryButton.Classes.Remove("accent");
                    _closeButton.Classes.Remove("accent");
                    if (Content is IControl cs && cs.Focusable)
                    {
                        cs.Focus();
                    }
                    else
                    {
                        _secondaryButton.Focus();
                    }

                    break;

                case ContentDialogButton.Close:
                    if (!_closeButton.IsVisible)
                        break;

                    _closeButton.Classes.Add("accent");
                    _primaryButton.Classes.Remove("accent");
                    _secondaryButton.Classes.Remove("accent");
                    if (Content is IControl cc && cc.Focusable)
                    {
                        cc.Focus();
                    }
                    else
                    {
                        _closeButton.Focus();
                    }

                    break;

                default:
                    _closeButton.Classes.Remove("accent");
                    _primaryButton.Classes.Remove("accent");
                    _secondaryButton.Classes.Remove("accent");

                    if (Content is IControl cd && cd.Focusable)
                    {
                        cd.Focus();
                    }
                    else if (_primaryButton.IsVisible)
                    {
                        _primaryButton.Focus();
                    }
                    else if (_secondaryButton.IsVisible)
                    {
                        _secondaryButton.Focus();
                    }
                    else if (_closeButton.IsVisible)
                    {
                        _closeButton.Focus();
                    }
                    else
                    {
                        Focus();
                    }

                    break;
            }
        }

        // Store the last element focused before showing the dialog, so we can
        // restore it when it closes
        private IInputElement _lastFocus;
        private IControl _originalHost;
        private int _originalHostIndex;
        private DialogHost _dialogHost;
        private ContentDialogResult result;
        private TaskCompletionSource<ContentDialogResult> tcs;
        private Button _primaryButton;
        private Button _secondaryButton;
        private Button _closeButton;
	}
}