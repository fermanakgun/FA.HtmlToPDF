# FA.HtmlToPDF

.NET 4.8, Windows. HTML  PDF. Harici NuGet bağımlılığı yok.

---

## Nasıl Çalışır

Birincil motor olarak **Chrome/Edge CDP** (`Page.printToPDF`) kullanır.  
Chrome bulunamazsa `System.Windows.Forms.WebBrowser` + bitmap pipeline'a düşer.

```
HtmlToPdfConverter
   HtmlContentPreprocessor       path normalize, kırık CSS <link> temizle
       ChromiumPdfEngine          CDP: Page.printToPDF (printBackground:true)
       WebBrowserHtmlRenderer     Fallback: IE Trident  bitmap  PDF
```

---

## Kurulum

.NET Framework 4.8 + Google Chrome veya Microsoft Edge kurulu olmalı.

```bash
dotnet build FA.HtmlToPDF.sln -c Release
```

---

## Kullanım

```csharp
using FA.HtmlToPDF;

// byte[] döndür
byte[] pdf = HtmlToPdfConverter.ConvertToBytes("<html>...</html>");

// Dosyaya kaydet
HtmlToPdfConverter.SaveToFile("<html>...</html>", @"C:\output\out.pdf");
```

### Seçenekler

```csharp
var options = new HtmlToPdfOptions
{
    // Boyutlar PDF point cinsinden (0 = HTML içeriğine göre otomatik)
    PageWidth  = 0f,
    PageHeight = 0f,

    // Kenar boşlukları (PDF point, 72 pt = 1 inch)
    MarginTop    = 0f,
    MarginBottom = 0f,
    MarginLeft   = 0f,
    MarginRight  = 0f,

    TimeoutMs              = 45000,   // Chrome timeout (ms)
    ChromiumExecutablePath = null,    // null = otomatik keşif
    HtmlBaseUrl            = null,    // <base href> olarak enjekte edilir
    PreferChromium         = true,
    FallbackToLegacyRenderer = true
};
```

`PageWidth` / `PageHeight` = `0` (varsayılan) olduğunda boyut, Chrome'un `Page.getLayoutMetrics` sonucundan otomatik hesaplanır; HTML'de ne varsa o kadar sayfa üretilir.

---

## HtmlToPdfOptions

| Özellik | Tip | Varsayılan | Açıklama |
|---|---|---|---|
| `PageWidth` | `float` | `0` | PDF point. `0` = otomatik |
| `PageHeight` | `float` | `0` | PDF point. `0` = otomatik |
| `MarginTop/Bottom/Left/Right` | `float` | `0` | PDF point |
| `TimeoutMs` | `int` | `45000` | Chrome CDP timeout |
| `ChromiumExecutablePath` | `string` | `null` | `null` = Program Files'ta ara |
| `HtmlBaseUrl` | `string` | `null` | Relative path'ler için base URL |
| `PreferChromium` | `bool` | `true` | CDP motorunu dene |
| `FallbackToLegacyRenderer` | `bool` | `true` | Başarısız olursa IE'ye düş |

---

## CDP Pipeline

```
chrome --headless=new --remote-debugging-port=<port>
  
   Page.enable
   Page.navigate("file:///input.html")
   wait: Page.loadEventFired
   Runtime.evaluate("document.fonts.ready", awaitPromise:true)
   Page.getLayoutMetrics             auto page size (PageWidth/Height = 0 ise)
   Page.printToPDF({ printBackground:true, paperWidth, paperHeight, margins })
         base64  byte[]
```

`printBackground: true`  CSS border, background-color, box-shadow gibi tüm görsel özelliklerin PDF'e yansıması için zorunlu parametredir. CLI `--print-to-pdf` flag'inin bu parametreye karşılığı yoktur.

---

## Performans

> **Ortam:** 11th Gen Intel Core i5-1135G7 @ 2.40 GHz  Windows 11 Enterprise  Release build  
> **Giriş:** Banka makbuzu HTML (~5 KB, satır içi CSS, tablo yapısı, Türkçe metin)  
> **Ölçüm:** 5 ardışık soğuk başlatma (her çalışmada Chrome yeniden başlatıldı)

| Metrik | Değer |
|---|---|
| Minimum | 4.870 ms |
| Maksimum | 5.469 ms |
| **Ortalama** | **5.183 ms** |
| Üretilen PDF boyutu | ~2.1 MB |

### Süre Dağılımı (Tahmini)

| Aşama | Süre |
|---|---|
| Chrome başlatma + CDP hazır | ~3.000 ms |
| `Page.navigate` + `loadEventFired` | ~300 ms |
| `document.fonts.ready` | ~100 ms |
| `Page.printToPDF` | ~700 ms |
| **Toplam** | **~4.95.5 s** |

Sürenin büyük bölümünü Chrome soğuk başlatma oluşturur. Aynı süreç içinde birden fazla dönüşüm yapılacaksa Chrome'u ısıtılmış tutmak (persistent CDP session) ortalamayı ~12 s'ye indirebilir.

---

## Proje Yapısı

```
FA.HtmlToPDF/
 Abstractions/
    IHtmlRenderer.cs
    IPdfDocumentBuilder.cs
 Models/
    HtmlToPdfOptions.cs
    ImageSlice.cs
 Pdf/
    PdfImageDocumentBuilder.cs
 Rendering/
    WebBrowserHtmlRenderer.cs
 Services/
    HtmlToPdfConverter.cs
    HtmlToPdfService.cs
 Utilities/
    ChromiumPdfEngine.cs
    HtmlContentPreprocessor.cs
 Runner/
     Startup.cs
```

---

## Lisans

[MIT](LICENSE)
