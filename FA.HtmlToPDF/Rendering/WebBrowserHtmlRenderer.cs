using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;
using FA.HtmlToPDF.Abstractions;
using FA.HtmlToPDF.Models;

namespace FA.HtmlToPDF.Rendering
{
    internal sealed class WebBrowserHtmlRenderer : IHtmlRenderer
    {
        public Bitmap Render(string html, HtmlToPdfOptions options)
        {
            Bitmap renderedBitmap = null;
            Exception failure = null;

            var thread = new Thread(() =>
            {
                try
                {
                    var completed = false;
                    var start = DateTime.UtcNow;

                    using (var form = new Form())
                    using (var browser = new WebBrowser())
                    using (var timer = new System.Windows.Forms.Timer())
                    {
                        form.ShowInTaskbar = false;
                        form.WindowState = FormWindowState.Normal;
                        form.FormBorderStyle = FormBorderStyle.None;
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-30000, -30000);
                        form.Opacity = 0;
                        form.Width = options.BrowserViewportWidth;
                        form.Height = 900;

                        browser.ScriptErrorsSuppressed = true;
                        browser.ScrollBarsEnabled = false;
                        browser.Dock = DockStyle.Fill;
                        browser.Width = options.BrowserViewportWidth;
                        browser.Height = 900;

                        form.Controls.Add(browser);

                        Action finish = () =>
                        {
                            if (!completed)
                            {
                                completed = true;
                                timer.Stop();
                                form.Close();
                            }
                        };

                        timer.Interval = 100;
                        timer.Tick += (sender, args) =>
                        {
                            if (completed) return;

                            var elapsedMs = (DateTime.UtcNow - start).TotalMilliseconds;
                            if (elapsedMs > options.RenderTimeoutMs)
                            {
                                failure = new TimeoutException("HTML render timed out.");
                                finish();
                                return;
                            }
                        };

                        browser.DocumentCompleted += (sender, args) =>
                        {
                            if (completed) return;
                            if (browser.ReadyState != WebBrowserReadyState.Complete || browser.Document == null || browser.Document.Body == null)
                            {
                                return;
                            }

                            try
                            {
                                var bodyRect = browser.Document.Body.ScrollRectangle;
                                var width = options.BrowserViewportWidth;
                                var height = Math.Max(1, bodyRect.Height);

                                width = Math.Min(width, Math.Min(options.MaxRenderWidthPx, 3000));
                                height = Math.Min(height, Math.Min(options.MaxRenderHeightPx, 8000));
                                width = Math.Max(1, width);
                                height = Math.Max(1, height);

                                browser.Width = width;
                                browser.Height = height;

                                renderedBitmap = TryRenderBitmap(browser, width, height);

                                finish();
                            }
                            catch (Exception ex)
                            {
                                failure = ex;
                                finish();
                            }
                        };

                        form.Shown += (sender, args) =>
                        {
                            timer.Start();
                            browser.DocumentText = html;
                        };

                        Application.Run(form);
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
            {
                throw new InvalidOperationException("HTML CSS ile render edilemedi.", failure);
            }

            if (renderedBitmap == null)
            {
                throw new InvalidOperationException("Render sonucu boş bitmap döndü.");
            }

            return renderedBitmap;
        }

        private static Bitmap TryRenderBitmap(WebBrowser browser, int width, int height)
        {
            var currentWidth = width;
            var currentHeight = height;
            Exception lastError = null;

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using (var bitmap = new Bitmap(currentWidth, currentHeight, PixelFormat.Format24bppRgb))
                    {
                        browser.DrawToBitmap(bitmap, new Rectangle(0, 0, currentWidth, currentHeight));
                        return (Bitmap)bitmap.Clone();
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    currentWidth = Math.Max(1, currentWidth / 2);
                    currentHeight = Math.Max(1, currentHeight / 2);
                    browser.Width = currentWidth;
                    browser.Height = currentHeight;
                }
            }

            throw new InvalidOperationException("WebBrowser görüntüsü bitmap olarak alınamadı.", lastError);
        }
    }
}
