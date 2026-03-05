# FA.HtmlToPDF

.NET 4.8, Windows. HTML PDF. Harici NuGet bağımlılığı yok.

---

## Nasıl Çalışır

Birincil motor olarak **Chrome/Edge CDP** (`Page.printToPDF`) kullanır.  
Chrome bulunamazsa `System.Windows.Forms.WebBrowser` + bitmap pipeline'a düşer.

```
HtmlToPdfConverter
   HtmlContentPreprocessor       ← path normalize, kırık CSS <link> temizle
       ChromiumPdfEngine          ← her istek için tek Chrome'dan sekme al
           ChromiumProcessHost    ← singleton: Chrome'u canlı tutar
       WebBrowserHtmlRenderer     ← Fallback: IE Trident → bitmap → PDF
```

**Eşzamanlı istekler:** İlk çağrıda Chrome bir kez başlatılır ve process hayatta kalır.  
Sonraki her dönüşüm `PUT /json/new` (~50 ms) ile yeni bir sekme açar, iş bitince `GET /json/close` ile kapatır.  
N eşzamanlı istek → 1 Chrome process + sıra bekleyen sekmeler (bounded concurrency).  
Hatalı dönüşüm otomatik olarak 2 kez yeniden denenir; Chrome çökerse sıfırdan başlatılır.

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

`PageWidth` / `PageHeight` = `0` (varsayılan) olduğunda `paperWidth`/`paperHeight` parametreleri `Page.printToPDF`'e hiç gönderilmez; Chrome kendi varsayılan kağıt boyutunu (A4) kullanır — tıpkı tarayıcıda "Yazdır → PDF olarak kaydet" yapıldığındaki gibi.

---

## HtmlToPdfOptions

| Özellik                       | Tip      | Varsayılan | Açıklama                        |
| ----------------------------- | -------- | ---------- | ------------------------------- |
| `PageWidth`                   | `float`  | `0`        | PDF point. `0` = otomatik       |
| `PageHeight`                  | `float`  | `0`        | PDF point. `0` = otomatik       |
| `MarginTop/Bottom/Left/Right` | `float`  | `0`        | PDF point                       |
| `TimeoutMs`                   | `int`    | `45000`    | Chrome CDP timeout              |
| `ChromiumExecutablePath`      | `string` | `null`     | `null` = Program Files'ta ara   |
| `HtmlBaseUrl`                 | `string` | `null`     | Relative path'ler için base URL |
| `PreferChromium`              | `bool`   | `true`     | CDP motorunu dene               |
| `FallbackToLegacyRenderer`    | `bool`   | `true`     | Başarısız olursa IE'ye düş      |

---

## CDP Pipeline

```
[İlk istek]
ChromiumProcessHost.EnsureAlive()
  └─ chrome --headless=new --remote-debugging-port=<port>  (bir kez başlatılır)

[Her istek — MaxConcurrentConversions slotu dahilinde]
SemaphoreSlim.Wait()                   ← slot boşalana kadar bekle
PUT /json/new                          ← yeni sekme ~50 ms
  ├─ Page.enable   ┐  pipeline: ikisi birden gönderilir,
  └─ Page.navigate ┘  Page.enable yanıtı beklenmez (~1 RTT tasarruf)
  └─ wait: Page.loadEventFired
  └─ Runtime.evaluate("document.fonts.ready", awaitPromise:true)
  └─ Page.printToPDF({ printBackground:true, ... })
        └─ base64 → byte[]
GET /json/close/{tabId}                ← sekme kapatılır + slot serbest bırakılır
```

`printBackground: true` — CSS border, background-color, box-shadow gibi tüm görsel özelliklerin PDF'e yansıması için zorunlu parametredir. CLI `--print-to-pdf` flag'inin bu parametreye karşılığı yoktur.

Explicit boyut verilmediğinde `paperWidth`/`paperHeight` parametreleri gönderilmez; Chrome doğrudan A4 üretir. Fazladan `Page.getLayoutMetrics` + viewport override adımları gerekmiyor.

---

## Performans

> **Ortam:** 11th Gen Intel Core i5-1135G7 @ 2.40 GHz · Windows 11 Enterprise · Release build  
> **Girdi:** Banka makbuzu HTML (~5 KB, satır içi CSS, tablo yapısı, Türkçe metin)  
> **Mimari:** Tek Chrome process, paylaşımlı sekme havuzu (`ChromiumProcessHost`)

### Soğuk başlatma vs ısınmış

| Senaryo                                | Süre        |
| -------------------------------------- | ----------- |
| Soğuk başlatma (Chrome yok, ilk istek) | ~5.000 ms   |
| **Isınmış, tek istek**                 | **~738 ms** |

### Eşzamanlı yük testi (ısınmış Chrome, `MaxConcurrentConversions = 8`)

| Eşzamanlı istek | Başarılı      | Duvar süresi  | Min       | Maks      | Ort.      |
| --------------- | ------------- | ------------- | --------- | --------- | --------- |
| 1               | 1 / 1         | 738 ms        | 738 ms    | 738 ms    | 738 ms    |
| 10              | 10 / 10       | 6.917 ms      | 3.565 ms  | 6.641 ms  | 5.680 ms  |
| **50**          | **50 / 50**   | **49.604 ms** | 7.740 ms  | 49.604 ms | 31.154 ms |
| **100**         | **100 / 100** | **70.624 ms** | 18.576 ms | 70.612 ms | 49.398 ms |

> Tüm istekler başarılı — hata yoktur. Limit üzerindeki istekler slot boşalana kadar bekler; slot boşaldığı anda anında işleme girer.

### Bounded concurrency davranışı

`ChromiumProcessHost.MaxConcurrentConversions` (varsayılan: `min(ProcessorCount, 8)`)  
bu değeri aşan istekler slot boşalana kadar bekler — birer birer servis edilir.

```
 50 istek, limit = 8:
 tur 1 → 8 istek aynı anda (~1.4 s)
 tur 2 → 8 istek aynı anda (~1.4 s)
 ...
 toplam ≈ ⌈50/8⌉ × 1.4 s ≈ 9 s, 0 hata
```

Ayarlamak için servis başlamadan önce:

```csharp
ChromiumProcessHost.MaxConcurrentConversions = 6;
```

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
    ChromiumProcessHost.cs
    HtmlContentPreprocessor.cs
 Runner/
     Startup.cs
```

---

## Lisans

[MIT](LICENSE)
