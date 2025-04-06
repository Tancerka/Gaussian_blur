using Microsoft.Win32;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows;

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Sharpi;

namespace dzialaj_prosze
{
    public partial class MainWindow : Window
    {
        [DllImport(@"C:\Users\victo\source\repos\dzialaj_prosze\x64\Debug\JAAsm.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ApplyGaussianBlurAsm(byte[] src, byte[] dst, int x, int y, int stride, IntPtr kernel, int kernel_Length, int radius, int width);

        private Bitmap _originalImage;
        private Bitmap _blurredImage;
        private int _threadCount = 1;

        public MainWindow()
        {
            InitializeComponent();
            ThreadSlider.ValueChanged += ThreadSlider_ValueChanged;
            _threadCount = (int)ThreadSlider.Value;
        }

        private void LoadImageButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _originalImage = new Bitmap(openFileDialog.FileName);
                ApplyBlurButton.IsEnabled = true;
                SaveImageButton.IsEnabled = false;
                BlurredImageViewer.Source = null;
                OriginalImageViewer.Source = ConvertBitmapToImageSource(_originalImage);
            }
        }

        private bool _isProcessing = false;

        private async void ApplyBlurButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing || _originalImage == null)
                return;

            try
            {
                _isProcessing = true;
                ApplyBlurButton.IsEnabled = false;

                if (!CheckIfAnyCheckBoxIsChecked())
                {
                    MessageBox.Show("Nie wybrałeś żadnego języka programowania. Wybierz język, w którym chcesz, by algorytm się wykonał.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                int threads = (int)ThreadSlider.Value;
                int blurRadius = (int)BlurRadiusSlider.Value;

                if (AsmCheckBox.IsChecked == true)
                {
                    CSharpCheckBox.IsChecked = false;
                    _blurredImage = await Task.Run(() => ApplyGaussianBlurAsmWrapper(_originalImage, threads));
                }
                else if (CSharpCheckBox.IsChecked == true)
                {
                    AsmCheckBox.IsChecked = false;
                    var result = await Task.Run(() =>
                        Sharpi.ImageProcessor.ApplyGaussianBlur(_originalImage, blurRadius, threads));

                    _blurredImage = result.resultImage;

                    Dispatcher.Invoke(() =>
                    {
                        ExecutionTimeTextBlock.Text = $"Czas wykonania (C#): {result.executionTime} ticks";
                    });
                }

                if (_blurredImage != null)
                {
                    var source = ConvertBitmapToImageSource(_blurredImage);
                    if (source != null)
                    {
                        BlurredImageViewer.Source = source;
                        SaveImageButton.IsEnabled = true;
                    }
                    else
                    {
                        MessageBox.Show("Błąd: Konwersja do ImageSource nie powiodła się!");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isProcessing = false;
                ApplyBlurButton.IsEnabled = true;
            }
        }

        private void SaveImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_blurredImage != null)
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "PNG Image|*.png"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    _blurredImage.Save(saveFileDialog.FileName, ImageFormat.Png);
                    MessageBox.Show("Image saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private bool CheckIfAnyCheckBoxIsChecked()
        {
            return CSharpCheckBox.IsChecked == true || AsmCheckBox.IsChecked == true;
        }

        private Bitmap ApplyGaussianBlurAsmWrapper(Bitmap image, int threadCount)
        {
            int width = image.Width;
            int height = image.Height;
            Bitmap result = new Bitmap(width, height);

            BitmapData srcData = image.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            BitmapData dstData = result.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                byte[] srcBytes = new byte[width * height * 4];
                byte[] dstBytes = new byte[width * height * 4];
                Marshal.Copy(srcData.Scan0, srcBytes, 0, srcBytes.Length);

                int radius = 0;
                Dispatcher.Invoke(() => { radius = (int)BlurRadiusSlider.Value; });
                double[] kernel = GenerateGaussianKernel(radius);
                int kernelSize = 2 * radius + 1;

                IntPtr kernelPtr = Marshal.AllocHGlobal(kernel.Length * sizeof(double));
                Marshal.Copy(kernel, 0, kernelPtr, kernel.Length);

                Stopwatch stopwatch = Stopwatch.StartNew();

                var options = new ParallelOptions { MaxDegreeOfParallelism = threadCount };

                Parallel.For(0, height, options, y =>
                {
                    for (int x = 0; x < width; x++)
                    {
                        ApplyGaussianBlurAsm(
                            srcBytes,
                            dstBytes,
                            x,
                            y,
                            width * 4,
                            kernelPtr,
                            kernelSize,
                            radius,
                            width);
                    }
                });

                stopwatch.Stop();

                Dispatcher.Invoke(() =>
                {
                    ExecutionTimeTextBlock.Text = $"Czas wykonania (Asembler): {stopwatch.ElapsedTicks} ticks";
                });

                Marshal.Copy(dstBytes, 0, dstData.Scan0, dstBytes.Length);
                Marshal.FreeHGlobal(kernelPtr);
            }
            finally
            {
                image.UnlockBits(srcData);
                result.UnlockBits(dstData);
            }

            return result;
        }

        private BitmapSource ConvertBitmapToImageSource(Bitmap bitmap)
        {
            if (bitmap == null) return null;

            using (MemoryStream memoryStream = new MemoryStream())
            {
                bitmap.Save(memoryStream, ImageFormat.Png);
                memoryStream.Seek(0, SeekOrigin.Begin);
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                return bitmapImage;
            }
        }

        private void ThreadSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (e.OldValue != e.NewValue)
            {
                _threadCount = (int)e.NewValue;
            }
        }

        private double[] GenerateGaussianKernel(int radius)
        {
            int size = 2 * radius + 1;
            double[] kernel = new double[size * size];
            double sigma = radius / 3.0;
            double sum = 0.0;

            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    int index = (y + radius) * size + (x + radius);
                    kernel[index] = Math.Exp(-(x * x + y * y) / (2 * sigma * sigma));
                    sum += kernel[index];
                }
            }

            for (int i = 0; i < kernel.Length; i++)
            {
                kernel[i] /= sum;
            }

            return kernel;
        }
    }
}