# Fermanakgun.HtmlToPDF

[![NuGet](https://img.shields.io/nuget/v/Fermanakgun.HtmlToPDF.svg)](https://www.nuget.org/packages/Fermanakgun.HtmlToPDF)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/fermanakgun/FA.HtmlToPDF/blob/main/LICENSE)

Zero-dependency .NET 4.8 library that converts HTML to PDF using a headless **Chrome/Edge** process via Chrome DevTools Protocol (CDP).  
No wkhtmltopdf. No Puppeteer. No WebView2. No NuGet dependencies.

## Requirements

- .NET Framework 4.8
- Windows
- Google Chrome **or** Microsoft Edge installed on the host machine

## Installation

```
dotnet add package Fermanakgun.HtmlToPDF
```

## Quick Start

```csharp
using FA.HtmlToPDF;

// Returns PDF as byte[]
byte[] pdf = HtmlToPdfConverter.ConvertToBytes("<html><body><h1>Hello</h1></body></html>");

// Saves PDF directly to disk
HtmlToPdfConverter.SaveToFile("<html><body><h1>Hello</h1></body></html>", @"C:\output\result.pdf");
```

## Options

```csharp
var options = new HtmlToPdfOptions
{
    PageWidth                = 0f,      // PDF points (72 pt = 1 inch). 0 = Chrome default (A4)
    PageHeight               = 0f,      // PDF points. 0 = Chrome default (A4)
    MarginTop                = 0f,      // PDF points
    MarginBottom             = 0f,
    MarginLeft               = 0f,
    MarginRight              = 0f,
    TimeoutMs                = 45000,   // Chrome CDP timeout (ms)
    ChromiumExecutablePath   = null,    // null = auto-discover from Program Files
    HtmlBaseUrl              = null,    // injected as <base href> for relative paths
    PreferChromium           = true,
    FallbackToLegacyRenderer = true     // fallback to WebBrowser (IE Trident) if Chrome not found
};

byte[] pdf = HtmlToPdfConverter.ConvertToBytes(html, options);
```

| Property                      | Type     | Default | Description                                                 |
| ----------------------------- | -------- | ------- | ----------------------------------------------------------- |
| `PageWidth`                   | `float`  | `0`     | PDF points. `0` = Chrome default (A4)                       |
| `PageHeight`                  | `float`  | `0`     | PDF points. `0` = Chrome default (A4)                       |
| `MarginTop/Bottom/Left/Right` | `float`  | `0`     | PDF points (72 pt = 1 inch)                                 |
| `TimeoutMs`                   | `int`    | `45000` | Per-conversion Chrome CDP timeout                           |
| `ChromiumExecutablePath`      | `string` | `null`  | Path to `chrome.exe` / `msedge.exe`. `null` = auto-discover |
| `HtmlBaseUrl`                 | `string` | `null`  | Injected as `<base href>` for relative assets               |
| `PreferChromium`              | `bool`   | `true`  | Use CDP engine                                              |
| `FallbackToLegacyRenderer`    | `bool`   | `true`  | Fall back to IE Trident if Chrome unavailable               |

## Concurrent Conversions

Chrome is started once and shared across all requests. Each conversion gets its own isolated CDP tab.  
A bounded concurrency pool limits simultaneous tabs (default: `min(ProcessorCount, 8)`).  
Failed conversions retry automatically up to 3 times; crashed Chrome restarts transparently.

```csharp
// Tune before the first conversion
ChromiumProcessHost.MaxConcurrentConversions = 4;
```

## Performance

> Intel Core i5-1135G7 @ 2.40 GHz · Windows 11 · Release build · `MaxConcurrentConversions = 8`

| Concurrent | Successful | Wall time | Avg / req |
| ---------- | ---------- | --------- | --------- |
| 1          | 1 / 1      | 738 ms    | 738 ms    |
| 10         | 10 / 10    | 6,917 ms  | 5,680 ms  |
| 50         | 50 / 50    | 49,604 ms | 31,154 ms |
| 100        | 100 / 100  | 70,624 ms | 49,398 ms |

Zero errors at all concurrency levels. Cold start (first request, Chrome not yet running): ~5 s.

## Source & Issues

[https://github.com/fermanakgun/FA.HtmlToPDF](https://github.com/fermanakgun/FA.HtmlToPDF)
