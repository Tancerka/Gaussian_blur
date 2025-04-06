using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Sharpi
{
    public static class ImageProcessor
    {

        public static (long executionTime, Bitmap resultImage) ApplyGaussianBlur(Bitmap image, int radius, int threads)
        {
            int width = image.Width;
            int height = image.Height;
            Bitmap result = new Bitmap(width, height);

            BitmapData imageData = image.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb
            );

            BitmapData resultData = result.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb
            );

            IntPtr imagePointer = imageData.Scan0;
            IntPtr resultPointer = resultData.Scan0;

            double[] kernel = GenerateGaussianKernel(radius);
            int kernelSize = 2 * radius + 1;
            int kernelRadius = kernelSize / 2;

            Stopwatch stopwatch = Stopwatch.StartNew();

            Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = threads }, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    double r = 0, g = 0, b = 0;
                    double weightSum = 0;

                    for (int ky = -kernelRadius; ky <= kernelRadius; ky++)
                    {
                        for (int kx = -kernelRadius; kx <= kernelRadius; kx++)
                        {
                            int newX = x + kx;
                            int newY = y + ky;

                            if (newX >= 0 && newX < width && newY >= 0 && newY < height)
                            {
                                int pixelIndex = (newY * imageData.Stride) + (newX * 4);
                                byte bPixel = Marshal.ReadByte(imagePointer, pixelIndex);
                                byte gPixel = Marshal.ReadByte(imagePointer, pixelIndex + 1);
                                byte rPixel = Marshal.ReadByte(imagePointer, pixelIndex + 2);

                                double weight = kernel[(ky + kernelRadius) * kernelSize + (kx + kernelRadius)];
                                r += rPixel * weight;
                                g += gPixel * weight;
                                b += bPixel * weight;
                                weightSum += weight;
                            }
                        }
                    }

                    int resultIndex = (y * imageData.Stride) + (x * 4);
                    byte newR = (byte)(r / weightSum);
                    byte newG = (byte)(g / weightSum);
                    byte newB = (byte)(b / weightSum);

                    Marshal.WriteByte(resultPointer, resultIndex, newB);
                    Marshal.WriteByte(resultPointer, resultIndex + 1, newG);
                    Marshal.WriteByte(resultPointer, resultIndex + 2, newR);
                    Marshal.WriteByte(resultPointer, resultIndex + 3, 255);
                }
            });

            stopwatch.Stop();
            long executionTime = stopwatch.ElapsedTicks;

            image.UnlockBits(imageData);
            result.UnlockBits(resultData);

            return (executionTime, result);
        }

        private static double[] GenerateGaussianKernel(int radius)
        {
            int size = radius * 2 + 1;
            double[] kernel = new double[size * size];
            double sigma = radius / 3.0;
            double sum = 0.0;

            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    double value = Math.Exp(-(x * x + y * y) / (2 * sigma * sigma));
                    kernel[(y + radius) * size + (x + radius)] = value;
                    sum += value;
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