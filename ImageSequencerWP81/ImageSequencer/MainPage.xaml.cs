﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using Lumia.Imaging;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Threading.Tasks;
using Lumia.Imaging.Transforms;

namespace ImageSequencer
{
    public partial class MainPage : PhoneApplicationPage
    {
        private ApplicationBarIconButton _playButton;
        private ApplicationBarIconButton _alignButton;
        private ApplicationBarIconButton _frameButton;
        private ApplicationBarIconButton _saveButton;

        private IReadOnlyList<IImageProvider> _unalignedImageProviders;
        private IReadOnlyList<IImageProvider> _alignedImageProviders;
        private IReadOnlyList<IImageProvider> _onScreenImageProviders;

        private WriteableBitmap _backgroundImage;
        private WriteableBitmap _foregroundImage;

        private bool _alignEnabled;
        private bool _frameEnabled;

        private int _animationIndex = 0;
        private DispatcherTimer _animationTimer;
        private volatile bool _rendering;
        private volatile bool _saveAfterRender;

        private Point _dragStart;

        private RectangleGeometry _animatedArea;

        // Constructor
        public MainPage()
        {
            InitializeComponent();
            InitializeApplicationBar();

            _animationTimer = new DispatcherTimer();
            _animationTimer.Tick += AnimationTimer_Tick;
            _animationTimer.Interval = new TimeSpan(0, 0, 0, 0, 100);
        }

        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            base.OnNavigatingFrom(e);

            Stop();
        }

        protected override void OnNavigatedTo(System.Windows.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            String sequenceIdParam;
            int sequenceId = 1;

            if (NavigationContext.QueryString.TryGetValue("sequenceId", out sequenceIdParam))
            {
                sequenceId = Convert.ToInt32(sequenceIdParam);
            }

            List<IImageProvider> imageSequence = CreateImageSequenceFromResources(sequenceId);

            SetImageSequence(imageSequence);
        }

        private void InitializeApplicationBar()
        {
            ApplicationBar = new ApplicationBar();

            _playButton = new ApplicationBarIconButton
            {
                IconUri = new Uri("/Assets/appbar.play.png", UriKind.Relative),
                Text = "play"
            };
            _playButton.Click += PlayButton_Click;
            ApplicationBar.Buttons.Add(_playButton);

            _alignButton = new ApplicationBarIconButton
            {
                IconUri = new Uri(@"Assets/appbar.align.disabled.png", UriKind.Relative),
                Text = "align"
            };
            _alignButton.Click += AlignButton_Click;
            ApplicationBar.Buttons.Add(_alignButton);

            _frameButton = new ApplicationBarIconButton
            {
                IconUri = new Uri(@"Assets/appbar.frame.disabled.png", UriKind.Relative),
                Text = "frame"
            };
            _frameButton.Click += FrameButton_Click;
            ApplicationBar.Buttons.Add(_frameButton);

            _saveButton = new ApplicationBarIconButton
            {
                IconUri = new Uri(@"Assets/appbar.save.png", UriKind.Relative),
                Text = "save"
            };
            _saveButton.Click += SaveButton_Click;
            ApplicationBar.Buttons.Add(_saveButton);

            var aboutMenuItem = new ApplicationBarMenuItem {Text = "about"};
            aboutMenuItem.Click += (s, e) => NavigationService.Navigate(new Uri("/AboutPage.xaml", UriKind.Relative));
            ApplicationBar.MenuItems.Add(aboutMenuItem);

        }

        private void SetControlsEnabled(bool enabled)
        {
            _playButton.IsEnabled = enabled;
            _alignButton.IsEnabled = enabled;
            _frameButton.IsEnabled = enabled;
            _saveButton.IsEnabled = enabled;
        }

        public async void SetImageSequence(List<IImageProvider> imageProviders)
        {
            ShowProgressIndicator("Aligning frames");
            SetControlsEnabled(false);

            _unalignedImageProviders = imageProviders;
            _onScreenImageProviders = _unalignedImageProviders;

            // Create aligned images
            using (ImageAligner imageAligner = new ImageAligner())
            {
                imageAligner.Sources = _unalignedImageProviders;
                imageAligner.ReferenceSource = _unalignedImageProviders[0];

                _alignedImageProviders = await imageAligner.AlignAsync();
            }

            // Create on-screen bitmap for rendering the image providers
            IImageProvider imageProvider = _onScreenImageProviders[0];
            ImageProviderInfo info = await imageProvider.GetInfoAsync();
            int width = (int)info.ImageSize.Width;
            int height = (int)info.ImageSize.Height;

            _foregroundImage = new WriteableBitmap(width, height);
            _backgroundImage = new WriteableBitmap(width, height);

            // Render the first frame of sequence
            await Render(0, true);

            InitializeAnimatedAreaBasedOnImageDimensions(width, height);

            SetControlsEnabled(true);
            HideProgressIndicator();
        }

        private void InitializeAnimatedAreaBasedOnImageDimensions(int imageWidth, int imageHeight)
        {
            if (_animatedArea == null)
            {
                _animatedArea = new RectangleGeometry();
                int offset = 5;
                AnimatedAreaIndicator.Width = Application.Current.Host.Content.ActualWidth - 1 - (offset * 2);
                AnimatedAreaIndicator.Height = imageHeight - (offset * 2);
                Canvas.SetLeft(AnimatedAreaIndicator, offset);
                Canvas.SetTop(AnimatedAreaIndicator, offset);
                _animatedArea.Rect = new Rect(0, 0, imageWidth, imageHeight);
            }
        }

        private List<IImageProvider> CreateImageSequenceFromResources(int sequenceId)
        {
            List<IImageProvider> imageProviders = new List<IImageProvider>();

            try
            {
                int i = 0;
                while (true)
                {
                    Uri uri = new Uri(@"Assets/Sequences/sequence." + sequenceId + "." + i + ".jpg", UriKind.Relative);
                    Stream stream = Application.GetResourceStream(uri).Stream;
                    StreamImageSource sis = new StreamImageSource(stream);
                    imageProviders.Add(new StreamImageSource(stream));
                    i++;
                }
            }
            catch (NullReferenceException ex)
            {
                // No more images available
            }

            return imageProviders;
        }

        private async void AnimationTimer_Tick(object sender, EventArgs eventArgs)
        {
            await Render(_animationIndex);

            if (_animationIndex == (_onScreenImageProviders.Count() - 1))
            {
                _animationIndex = 0;
            }
            else
            {
                _animationIndex++;
            }
        }

        private async Task Render(int animationIndex, bool refreshBackground = false)
        {
            if (!_rendering)
            {
                _rendering = true;

                using (WriteableBitmapRenderer writeableBitmapRenderer = new WriteableBitmapRenderer(_onScreenImageProviders[animationIndex], _foregroundImage))
                {
                    ImageElement.Source = await writeableBitmapRenderer.RenderAsync();
                }

                if (refreshBackground)
                {
                    using (WriteableBitmapRenderer writeableBitmapRenderer = new WriteableBitmapRenderer(_onScreenImageProviders[0], _backgroundImage))
                    {
                        ImageElementBackground.Source = await writeableBitmapRenderer.RenderAsync();
                    }
                }

                _rendering = false;
            }

            if (_saveAfterRender)
            {
                _saveAfterRender = false;
                Save();
            }

        }

        private void PlayButton_Click(object sender, EventArgs e)
        {
            if (_animationTimer.IsEnabled)
            {
                Stop();
            }
            else
            {
                Play();
            }
        }

        private void Stop()
        {
            _animationTimer.Stop();
            _playButton.IconUri = new Uri(@"Assets/appbar.play.png", UriKind.Relative);
        }

        private void Play()
        {
            _animationTimer.Start();
            _playButton.IconUri = new Uri(@"Assets/appbar.pause.png", UriKind.Relative);
        }

        private async void AlignButton_Click(object sender, EventArgs e)
        {
            _alignEnabled = !_alignEnabled;

            if (_alignEnabled)
            {
                _onScreenImageProviders = _alignedImageProviders;
                _alignButton.IconUri = new Uri(@"Assets/appbar.align.enabled.png", UriKind.Relative);
            }
            else
            {
                _onScreenImageProviders = _unalignedImageProviders;
                _alignButton.IconUri = new Uri(@"Assets/appbar.align.disabled.png", UriKind.Relative);
            }

            await Render(_animationIndex, true);

            _saveButton.IsEnabled = true;
        }

        private async void FrameButton_Click(object sender, EventArgs e)
        {
            _frameEnabled = !_frameEnabled;
            AnimatedAreaIndicator.Visibility = _frameEnabled ? Visibility.Visible : Visibility.Collapsed;

            if (!_frameEnabled)
            {
                ImageElement.Clip = null;
                _frameButton.IconUri = new Uri(@"Assets/appbar.frame.disabled.png", UriKind.Relative);
            }
            else
            {
                ImageElement.Clip = _animatedArea;
                _frameButton.IconUri = new Uri(@"Assets/appbar.frame.enabled.png", UriKind.Relative);
            }

            await Render(_animationIndex, true);

            _saveButton.IsEnabled = true;
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            _saveAfterRender = _rendering;

            if (!_saveAfterRender)
            {
                Save();
            }
        }

        private async void Save()
        {
            bool resumePlaybackAfterSave = _animationTimer.IsEnabled;

            Stop();

            SetControlsEnabled(false);

            ShowProgressIndicator("Saving");

            if (_frameEnabled)
            {
                await GifExporter.Export(_onScreenImageProviders, _animatedArea.Rect);
            }
            else
            {
                await GifExporter.Export(_onScreenImageProviders, null);
            }

            HideProgressIndicator();

            if (resumePlaybackAfterSave)
            {
                Play();
            }

            SetControlsEnabled(true);
            _saveButton.IsEnabled = false;
        }

        private void ShowProgressIndicator(String text)
        {
            SystemTray.ProgressIndicator = new ProgressIndicator {Text = text, IsIndeterminate = true, IsVisible = true};
        }

        private void HideProgressIndicator()
        {
            if (SystemTray.ProgressIndicator != null)
            {
                SystemTray.ProgressIndicator.IsVisible = false;
            }
        }

        private void ImageElement_ManipulationDelta(object sender, System.Windows.Input.ManipulationDeltaEventArgs e)
        {
            if (_frameEnabled)
            {
                double x0 = Math.Min(e.ManipulationOrigin.X, _dragStart.X);
                double x1 = Math.Max(e.ManipulationOrigin.X, _dragStart.X);
                double y0 = Math.Min(e.ManipulationOrigin.Y, _dragStart.Y);
                double y1 = Math.Max(e.ManipulationOrigin.Y, _dragStart.Y);

                x0 = Math.Max(x0, 0);
                x1 = Math.Min(x1, _foregroundImage.PixelWidth);
                y0 = Math.Max(y0, 0);
                y1 = Math.Min(y1, _foregroundImage.PixelHeight);

                double width = x1 - x0;
                double height = y1 - y0;

                Rect rect = new Rect(x0, y0, width, height);
                Canvas.SetLeft(AnimatedAreaIndicator, rect.X);
                Canvas.SetTop(AnimatedAreaIndicator, rect.Y);
                AnimatedAreaIndicator.Width = rect.Width;
                AnimatedAreaIndicator.Height = rect.Height;

                _animatedArea.Rect = rect;

                _saveButton.IsEnabled = true;
            }
        }

        private void ImageElement_ManipulationStarted(object sender, System.Windows.Input.ManipulationStartedEventArgs e)
        {
            _dragStart = new Point(e.ManipulationOrigin.X, e.ManipulationOrigin.Y);
        }

    }
}