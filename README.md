# FA.HtmlToPDF

.NET 4.8 tabanlı, HTML içeriğini PDF'e dönüştüren bir Windows kütüphanesidir. Birincil render motoru olarak **Chrome DevTools Protocol (CDP)** kullanır; bu sayede CSS border, background, font ve layout özellikleri PDF'de tam olarak yansıtılır. Chrome/Edge bulunamazsa eski `WebBrowser` (IE Trident) motoruna otomatik olarak geri döner.

---

## Özellikler

- **Chromium tabanlı render** — `Page.printToPDF` (CDP) ile Playwright ile aynı yöntemi kullanır; `printBackground: true` sayesinde tüm CSS özellikleri (border, background-color vb.) PDF'e aktarılır
- **Fallback desteği** — Chrome/Edge bulunamazsa `System.Windows.Forms.WebBrowser` (IE) motoruna geçer
- **Türkçe karakter desteği** — UTF-16BE hex string encoding ile PDF metadata dahil tam Türkçe karakter desteği
- **Çift API** — `byte[]` döndürme + dosyaya kaydetme yöntemleri
- **Otomatik Chrome/Edge keşfi** — Program Files, Program Files (x86), LocalAppData konumlarında otomatik arama
- **Eksik yerel CSS temizleme** — Var olmayan `file://` CSS `<link>` etiketleri render'dan önce otomatik temizlenir
- **Yapılandırılabilir** — Kağıt boyutu, margin, timeout, Chromium yolu ve PDF metadata ayarlanabilir
- **Design Patterns** — Facade (`HtmlToPdfConverter`) + Strategy (`IHtmlRenderer`, `IPdfDocumentBuilder`)

---

## Proje Yapısı

```
FA.HtmlToPDF/
├── Abstractions/
│   ├── IHtmlRenderer.cs          # Render motoru arayüzü
│   └── IPdfDocumentBuilder.cs    # PDF oluşturucu arayüzü
├── Models/
│   ├── HtmlToPdfOptions.cs       # Tüm yapılandırma seçenekleri
│   └── ImageSlice.cs             # Fallback renderer için görüntü dilimleme
├── Pdf/
│   └── PdfImageDocumentBuilder.cs # Bitmap tabanlı PDF oluşturucu (fallback)
├── Rendering/
│   └── WebBrowserHtmlRenderer.cs  # IE WebBrowser render motoru (fallback)
├── Samples/
│   └── HtmlSamples.cs            # Örnek makbuz HTML'i
├── Services/
│   ├── HtmlToPdfConverter.cs     # Public Facade (static giriş noktası)
│   └── HtmlToPdfService.cs       # Çekirdek servis
├── Utilities/
│   ├── ChromiumPdfEngine.cs      # CDP tabanlı Chromium PDF motoru
│   ├── HtmlContentPreprocessor.cs # HTML ön işleme (path normalize, CSS temizleme)
│   └── PdfEncodingHelper.cs      # PDF Unicode string encoding
└── Runner/
    ├── Startup.cs                # Konsol uygulama giriş noktası
    └── FA.HtmlToPDF.Runner.csproj
```

---

## Kurulum

### Gereksinimler

- .NET Framework 4.8
- Windows işletim sistemi
- Google Chrome veya Microsoft Edge (Chromium render için)

### Derleme

```bash
dotnet build FA.HtmlToPDF.sln -c Release
```

---

## Kullanım

### Temel Kullanım

```csharp
using FA.HtmlToPDF;

// byte[] olarak al
byte[] pdfBytes = HtmlToPdfConverter.ConvertToBytes("<html>...</html>");

// Dosyaya kaydet
HtmlToPdfConverter.SaveToFile("<html>...</html>", @"C:\output\belge.pdf");
```

### Seçeneklerle Kullanım

```csharp
using FA.HtmlToPDF;
using FA.HtmlToPDF.Models;

var options = new HtmlToPdfOptions
{
    // Kağıt boyutu (PDF point cinsinden, varsayılan: A4)
    PageWidth  = 595f,   // 210mm
    PageHeight = 842f,   // 297mm

    // Render motoru
    PreferChromium         = true,   // Chromium'u önce dene (varsayılan: true)
    FallbackToLegacyRenderer = true, // Chromium yoksa WebBrowser'a geç
    ChromiumTimeoutMs      = 45000,
    ChromiumExecutablePath = null,   // null = otomatik arama

    // PDF metadata
    Title    = "Makbuz",
    Author   = "Ziraat Bank",
    Subject  = "İşlem Makbuzu",
    Keywords = "makbuz, banka",
    Creator  = "MyApp",
    Producer = "FA.HtmlToPDF"
};

byte[] pdfBytes = HtmlToPdfConverter.ConvertToBytes(html, options);
```

### Örnek Makbuz PDF'i Üret

```csharp
HtmlToPdfConverter.SaveSampleReceiptPdf(@"C:\output\makbuz.pdf");
```

---

## HtmlToPdfOptions Referansı

| Özellik                       | Tür      | Varsayılan       | Açıklama                                   |
| ----------------------------- | -------- | ---------------- | ------------------------------------------ |
| `PageWidth`                   | `float`  | `595f`           | Kağıt genişliği (PDF point)                |
| `PageHeight`                  | `float`  | `842f`           | Kağıt yüksekliği (PDF point)               |
| `MarginTop/Bottom/Left/Right` | `float`  | `20f`            | Sayfa kenar boşlukları (PDF point)         |
| `PreferChromium`              | `bool`   | `true`           | Chromium render motorunu kullan            |
| `FallbackToLegacyRenderer`    | `bool`   | `true`           | Chromium başarısız olursa WebBrowser'a geç |
| `ChromiumTimeoutMs`           | `int`    | `45000`          | Chromium timeout (ms)                      |
| `ChromiumExecutablePath`      | `string` | `null`           | Chrome/Edge exe yolu (null = otomatik)     |
| `BrowserViewportWidth`        | `int`    | `980`            | Fallback renderer viewport genişliği (px)  |
| `RenderTimeoutMs`             | `int`    | `20000`          | Fallback renderer timeout (ms)             |
| `JpegQuality`                 | `long`   | `92`             | Fallback renderer JPEG kalitesi (1-100)    |
| `HtmlBaseUrl`                 | `string` | `null`           | HTML için base URL                         |
| `Title`                       | `string` | `"HTML to PDF"`  | PDF başlığı                                |
| `Author`                      | `string` | `null`           | PDF yazarı                                 |
| `Subject`                     | `string` | `null`           | PDF konusu                                 |
| `Keywords`                    | `string` | `null`           | PDF anahtar kelimeleri                     |
| `Creator`                     | `string` | `"FA.HtmlToPDF"` | PDF oluşturucu                             |
| `Producer`                    | `string` | `"FA.HtmlToPDF"` | PDF üretici                                |

---

## Mimari

### Render Pipeline

```
HtmlToPdfConverter (Facade)
        │
        ▼
HtmlToPdfService.ConvertToBytes()
        │
        ├─► HtmlContentPreprocessor.Prepare()
        │       • InjectBaseTag()
        │       • EnsureBrowserCompatibility()   (IE=edge meta)
        │       • NormalizeLocalFilePaths()       (C:\... → file://...)
        │       • StripMissingLocalCssLinks()     (var olmayan CSS link'leri kaldır)
        │
        ├─► [PreferChromium = true]
        │       ChromiumPdfEngine.TryConvert()
        │           • Chrome/Edge başlat (--remote-debugging-port)
        │           • CDP WebSocket bağlantısı aç
        │           • Page.navigate() → Page.loadEventFired bekle
        │           • Page.printToPDF({ printBackground: true })
        │           • Base64 PDF verisini decode et
        │
        └─► [Fallback]
                WebBrowserHtmlRenderer.Render()   → Bitmap
                PdfImageDocumentBuilder.Build()   → PDF byte[]
```

### Design Patterns

- **Facade**: `HtmlToPdfConverter` — tüm API'yi tek static sınıfta toplar
- **Strategy**: `IHtmlRenderer` + `IPdfDocumentBuilder` — render ve PDF oluşturma adımları birbirinden bağımsız, değiştirilebilir

---

## Nasıl Çalışır: CDP vs CLI

Pek çok HTML-to-PDF kütüphanesi Chrome'u `--print-to-pdf` CLI flag'i ile çalıştırır. Ancak bu yöntemde `printBackground` parametresi desteklenmez ve CSS border/background gibi özellikler PDF'de kaybolur.

Bu kütüphane [Playwright](https://github.com/microsoft/playwright) ile aynı yaklaşımı kullanır:

```
Chrome --remote-debugging-port=<port> &
  ↓
GET /json/list  →  WebSocket URL
  ↓
WS: Page.enable
WS: Page.navigate("file:///input.html")
WS: waitFor("Page.loadEventFired")
WS: Page.printToPDF({ printBackground: true, paperWidth: ..., ... })
  ↓
Base64 PDF → byte[]
```

`printBackground: true` — border, background-color ve box-shadow gibi tüm CSS özelliklerini PDF'e aktaran kritik parametredir.

---

## Runner (Konsol Uygulaması)

```bash
FA.HtmlToPDF.Runner.exe
```

Çalıştırıldığında:

1. Örnek makbuz HTML'ini hazırlar ve `output/debug-preview.html` olarak kaydeder
2. `debug-preview.html` dosyasını varsayılan tarayıcıda açar (layout doğrulama için)
3. Aynı HTML'den PDF üretir → `output/receipt-sample.pdf`

Çıktı dizini: `<exe dizini>/output/`

---

## Lisans

Bu proje [MIT Lisansı](LICENSE) ile lisanslanmıştır.

Kullanabilir, kopyalayabilir, değiştirebilir, birleştirebilir, yayımlayabilir, dağıtabilir, alt lisanslayabilir ve satabilirsiniz.
