using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Threading.Tasks;

namespace PhotoView.Controls;

public sealed partial class ImageViewerControl
{
    public void PrepareCloseAnimation()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        CancelHighResLoad();
        StopPhysics();

        try
        {
            if (_isLoaded)
            {
                AnimationImage.Source = MainImage.Source ?? AnimationImage.Source ?? _imageFileInfo?.Thumbnail;
                AnimationImage.Opacity = 1;
                ImageContainer.Opacity = 0;

                ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("BackConnectedAnimation", AnimationImage);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] PrepareCloseAnimation error: {ex}");
        }
        finally
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void HandleRatingKey(Windows.System.VirtualKey key)
    {
        if (key >= Windows.System.VirtualKey.Number1 && key <= Windows.System.VirtualKey.Number5)
        {
            var rating = (int)(key - Windows.System.VirtualKey.Number1 + 1);
            if (ViewModel != null)
            {
                _ = ViewModel.SetRatingCommand.ExecuteAsync(rating);
            }
        }
        else if (key == Windows.System.VirtualKey.Number0 && ViewModel != null)
        {
            _ = ViewModel.SetRatingCommand.ExecuteAsync(-1);
        }
    }

    public async Task CompleteCloseAsync()
    {
        try
        {
            if (!_isLoaded)
            {
                return;
            }

            var storyboard = new Storyboard();

            var fadeOutBackground = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300)
            };
            Storyboard.SetTarget(fadeOutBackground, BackgroundOverlay);
            Storyboard.SetTargetProperty(fadeOutBackground, "Opacity");
            storyboard.Children.Add(fadeOutBackground);

            var fadeOutContainer = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300)
            };
            Storyboard.SetTarget(fadeOutContainer, AnimationContainer);
            Storyboard.SetTargetProperty(fadeOutContainer, "Opacity");
            storyboard.Children.Add(fadeOutContainer);

            var tcs = new TaskCompletionSource<bool>();
            storyboard.Completed += (_, _) => tcs.TrySetResult(true);
            storyboard.Begin();
            await tcs.Task;

            if (_isLoaded)
            {
                Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] CompleteCloseAsync error: {ex}");
            try
            {
                Visibility = Visibility.Collapsed;
            }
            catch
            {
            }
        }
    }

    public FrameworkElement GetMainImage() => AnimationImage;

    public UIElement[] GetCoordinatedElements() => new UIElement[] { InfoPanel };

    private void BackgroundOverlay_Tapped(object sender, TappedRoutedEventArgs e)
    {
        PrepareCloseAnimation();
    }

    private void ImageContainer_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        Focus(FocusState.Programmatic);
    }

    private void InfoPanel_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        Focus(FocusState.Programmatic);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        PrepareCloseAnimation();
    }

    private void ImageTransformContainer_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_is1To1Scale)
        {
            _targetZoomScale = 1.0;
            _is1To1Scale = false;
        }
        else
        {
            _targetZoomScale = CalculateOriginalScale();
            _is1To1Scale = true;
        }

        _hasZoomAnchor = false;
        TryStartOriginalImageLoad();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        try
        {
            if (e.Key >= Windows.System.VirtualKey.Number1 && e.Key <= Windows.System.VirtualKey.Number5)
            {
                var rating = (uint)(e.Key - Windows.System.VirtualKey.Number1 + 1);
                if (ViewModel != null)
                {
                    _ = ViewModel.SetRatingCommand.ExecuteAsync(rating);
                    e.Handled = true;
                }
            }
            else if (e.Key == Windows.System.VirtualKey.Number0 && ViewModel != null)
            {
                _ = ViewModel.SetRatingCommand.ExecuteAsync(0);
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] OnKeyDown error: {ex}");
        }
    }
}
