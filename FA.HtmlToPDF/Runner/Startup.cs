using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FA.HtmlToPDF;
using FA.HtmlToPDF.Models;

namespace FA.HtmlToPDF.Runner
{
    internal static class Startup
    {
        // Sample receipt HTML (Ziraat Bank / offrctl structure).
        // In real usage, pass your own HTML to HtmlToPdfConverter.SaveToFile / ConvertToBytes.
        private const string SampleHtml = @"<!DOCTYPE html>
<html lang=""tr"">
<head>
    <meta charset=""UTF-8"" />
    <title>Örnek PDF Test Dokümanı</title>
    <style>
        body {
            font-family: Arial, Helvetica, sans-serif;
            font-size: 13px;
            line-height: 1.5;
            color: #000;
            margin: 40px;
        }

        h1, h2, h3 {
            margin: 0 0 10px 0;
        }

        h1 {
            font-size: 24px;
            text-align: center;
            margin-bottom: 20px;
        }

        h2 {
            font-size: 18px;
            margin-top: 25px;
            border-bottom: 1px solid #ccc;
            padding-bottom: 5px;
        }

        p {
            margin: 0 0 10px 0;
            text-align: justify;
        }

        .info-table,
        .data-table {
            width: 100%;
            border-collapse: collapse;
            margin: 15px 0 20px 0;
        }

        .info-table td {
            padding: 6px 8px;
            border: 1px solid #999;
        }

        .data-table th,
        .data-table td {
            border: 1px solid #999;
            padding: 8px;
            text-align: left;
        }

        .data-table th {
            background: #f2f2f2;
        }

        .section {
            margin-bottom: 20px;
        }

        .signature {
            margin-top: 50px;
            width: 300px;
        }

        .signature-line {
            margin-top: 50px;
            border-top: 1px solid #000;
            padding-top: 5px;
            text-align: center;
        }

        ul {
            margin: 10px 0 10px 20px;
        }

        li {
            margin-bottom: 6px;
        }
    </style>
</head>
<body>

    <h1>Örnek PDF Test Dokümanı</h1>

    <table class=""info-table"">
        <tr>
            <td><strong>Doküman No</strong></td>
            <td>PDF-TEST-2026-001</td>
            <td><strong>Tarih</strong></td>
            <td>06.03.2026</td>
        </tr>
        <tr>
            <td><strong>Müşteri Adı</strong></td>
            <td>Örnek Müşteri A.Ş.</td>
            <td><strong>Hazırlayan</strong></td>
            <td>Test Kullanıcısı</td>
        </tr>
        <tr>
            <td><strong>Doküman Türü</strong></td>
            <td>Bilgilendirme / Test</td>
            <td><strong>Durum</strong></td>
            <td>Taslak</td>
        </tr>
    </table>

    <div class=""section"">
        <h2>1. Amaç</h2>
        <p>
            Bu doküman, HTML içeriğinin PDF çıktısına dönüştürülmesi sırasında metin hizalaması,
            başlık görünümü, tablo yapısı, satır kırılımı, sayfa taşması ve genel yazdırma kalitesinin
            test edilmesi amacıyla hazırlanmıştır. İçerik özellikle sade tutulmuş, ancak gerçek bir
            iş dokümanına benzeyecek şekilde örnek metin, tablo ve liste alanları eklenmiştir.
        </p>
        <p>
            Test sırasında özellikle sayfa kenar boşlukları, yazı tipi boyutları, tablo sütun genişlikleri,
            paragraf akışı ve varsa sayfa sonlarında oluşan bozulmalar kontrol edilmelidir. Bu yapı,
            hem tarayıcı yazdırma senaryolarında hem de HTML-to-PDF kütüphaneleri ile yapılan dönüşümlerde
            kullanılabilecek genel bir örnek olarak düşünülebilir.
        </p>
    </div>

    <div class=""section"">
        <h2>2. Genel Açıklamalar</h2>
        <p>
            Doküman içerisinde yer alan başlıklar ve alt başlıklar, farklı içerik bloklarının PDF üzerinde
            nasıl konumlandığını görmek için eklenmiştir. Paragraflar, normal iş metni uzunluğuna yakın
            olacak şekilde hazırlanmıştır. Bu sayede tek sayfa içinde çok kısa kalmayan, fakat gereksiz şekilde
            de uzamayan bir içerik elde edilmiştir.
        </p>
        <p>
            Aşağıda örnek bir veri tablosu paylaşılmıştır. Bu tablo; satır yükseklikleri, kenarlıkların
            görünürlüğü, hücre içeriğinin taşma davranışı ve farklı metin uzunluklarının baskı üzerindeki
            etkisini değerlendirmek için kullanılabilir.
        </p>
    </div>

    <div class=""section"">
        <h2>3. Örnek Veri Tablosu</h2>
        <table class=""data-table"">
            <thead>
                <tr>
                    <th>#</th>
                    <th>Hizmet Adı</th>
                    <th>Açıklama</th>
                    <th>Tutar</th>
                    <th>Durum</th>
                </tr>
            </thead>
            <tbody>
                <tr>
                    <td>1</td>
                    <td>Analiz Çalışması</td>
                    <td>İlk analiz ve ihtiyaç toplama süreci tamamlandı.</td>
                    <td>5.000 TL</td>
                    <td>Tamamlandı</td>
                </tr>
                <tr>
                    <td>2</td>
                    <td>Tasarım Hazırlığı</td>
                    <td>Ekran taslakları ve kullanıcı akışları oluşturuldu.</td>
                    <td>7.500 TL</td>
                    <td>Devam Ediyor</td>
                </tr>
                <tr>
                    <td>3</td>
                    <td>Geliştirme</td>
                    <td>Temel modüller için kodlama süreci planlandı.</td>
                    <td>18.000 TL</td>
                    <td>Beklemede</td>
                </tr>
                <tr>
                    <td>4</td>
                    <td>Test ve Doğrulama</td>
                    <td>Fonksiyonel ve teknik test senaryoları hazırlanacak.</td>
                    <td>4.250 TL</td>
                    <td>Beklemede</td>
                </tr>
            </tbody>
        </table>
    </div>

    <div class=""section"">
        <h2>4. Kontrol Maddeleri</h2>
        <ul>
            <li>Başlıklar sayfa üzerinde düzgün hizalanıyor mu?</li>
            <li>Tablo kenarlıkları PDF çıktısında net görünüyor mu?</li>
            <li>Uzun metinler satır sonlarında düzgün şekilde bölünüyor mu?</li>
            <li>Sayfa altına yaklaşan içerik bir sonraki sayfaya temiz geçiyor mu?</li>
            <li>Türkçe karakterlerde bozulma ya da encoding problemi oluşuyor mu?</li>
            <li>Paragraf ve tablo aralıkları beklenen şekilde korunuyor mu?</li>
        </ul>
    </div>

    <div class=""section"">
        <h2>5. Sonuç</h2>
        <p>
            Bu örnek HTML içerik, temel bir PDF üretim testinde kullanılabilecek yeterli yoğunlukta veri sağlar.
            Başlık, paragraf, tablo ve liste gibi yaygın doküman öğelerini bir arada içerdiği için hem görsel
            düzen hem de teknik çıktı kalitesi açısından faydalı bir referans sunar.
        </p>
        <p>
            İhtiyaç olması halinde bu yapı daha uzun hale getirilerek 3–4 sayfalık test dokümanına dönüştürülebilir
            ya da tam tersine daha kısa hale getirilerek tek sayfalık minimal bir PDF örneği olarak da kullanılabilir.
        </p>
    </div>

    <div class=""signature"">
        <div class=""signature-line"">İmza / Onay</div>
    </div>

</body>
</html>";

        private static void Main(string[] args)
        {
            var outputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
            Directory.CreateDirectory(outputDirectory);

            if (args.Length > 0 && args[0] == "--bench")
            {
                RunBenchmark(outputDirectory);
                return;
            }

            var outputFilePath = Path.Combine(outputDirectory, "receipt-sample.pdf");
            HtmlToPdfConverter.SaveToFile(SampleHtml, outputFilePath);
            Console.WriteLine("PDF oluşturuldu: " + outputFilePath);
        }

        private static void RunBenchmark(string outputDirectory)
        {
            // Benchmark seçenekleri: fallback kapalı (Chrome hatası direkt gelsin),
            // yüksek eşzamanlılık için timeout artırıldı.
            var benchOptions = new HtmlToPdfOptions
            {
                PreferChromium = true,
                FallbackToLegacyRenderer = false,
                TimeoutMs = 120000
            };

            // --- Isınma: Chrome'u ayağa kaldır ---
            Console.WriteLine("[BENCH] Isınma (1 dönüşüm)...");
            HtmlToPdfConverter.ConvertToBytes(SampleHtml, benchOptions);
            Console.WriteLine("[BENCH] Chrome hazır.\n");

            foreach (var concurrency in new[] { 1, 10, 50, 100 })
            {
                var times = new long[concurrency];
                var errors = new string[concurrency];
                var tasks = new Task[concurrency];

                var wallSw = Stopwatch.StartNew();

                for (var i = 0; i < concurrency; i++)
                {
                    var idx = i;
                    tasks[i] = Task.Run(() =>
                    {
                        var sw = Stopwatch.StartNew();
                        try
                        {
                            HtmlToPdfConverter.ConvertToBytes(SampleHtml, benchOptions);
                        }
                        catch (Exception ex)
                        {
                            var inner = ex;
                            while (inner.InnerException != null) inner = inner.InnerException;
                            errors[idx] = inner.Message;
                        }
                        sw.Stop();
                        times[idx] = sw.ElapsedMilliseconds;
                    });
                }

                Task.WaitAll(tasks);
                wallSw.Stop();

                var successTimes = times.Where((t, i) => errors[i] == null).ToArray();
                var errorCount = errors.Count(e => e != null);

                Console.WriteLine("=== Eşzamanlı: " + concurrency + " istek ===");
                if (successTimes.Length > 0)
                {
                    Console.WriteLine("  Başarılı  : " + successTimes.Length + " / " + concurrency);
                    Console.WriteLine("  Min       : " + successTimes.Min() + " ms");
                    Console.WriteLine("  Max       : " + successTimes.Max() + " ms");
                    Console.WriteLine("  Ort.      : " + (long)successTimes.Average() + " ms");
                }
                else
                {
                    Console.WriteLine("  Başarılı  : 0 / " + concurrency);
                }
                if (errorCount > 0)
                    Console.WriteLine("  Hata      : " + errorCount + "  [" + errors.First(e => e != null) + "]");
                Console.WriteLine("  Duvar     : " + wallSw.ElapsedMilliseconds + " ms  (toplam geçen süre)");
                if (successTimes.Length > 0)
                    Console.WriteLine("  Verim     : " + (successTimes.Length * 1000 / wallSw.ElapsedMilliseconds) + " dönüşüm/s");
                Console.WriteLine();
            }
        }
    }
}
