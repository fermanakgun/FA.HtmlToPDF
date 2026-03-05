namespace FA.HtmlToPDF.Samples
{
    public static class HtmlSamples
    {
        // This HTML matches the real application output (export-html / offrctl structure).
        // The external CSS bundles (MasterAssemblyCssBundle etc.) are not available on all
        // machines, so their CSS class definitions are inlined here so the layout renders
        // correctly everywhere without needing those files.
        public static string ReceiptHtml => @"<html class=""export-html"">   <head> <meta charset=""utf-8""> <link rel=""stylesheet"" type=""text/css"" href=""C:\Projects\VeriBranch.ZiraatBank.AZ\ZiraatDev\Source\Presentation.Web\WebApplication.UI\Content\css\bundled/MasterAssemblyCssBundle.css?v=2013927173121?r=32F3102C92F13518EE773D55632949CA"" /><link rel=""stylesheet"" type=""text/css"" href=""C:\Projects\VeriBranch.ZiraatBank.AZ\ZiraatDev\Source\Presentation.Web\WebApplication.UI\Content\css\bundled/MasterCssBundle.css?v=2013927173121?r=1BD5BD07BA18A325E9CF81AEEE9616A0"" /><link rel=""stylesheet"" type=""text/css"" href=""C:\Projects\VeriBranch.ZiraatBank.AZ\ZiraatDev\Source\Presentation.Web\WebApplication.UI\Content\css\bundled/VeriBranchMasterCssBundle.css?v=2013927173121?r=D24A8E3CB8CBFD80FAA5EC87926DAAC8"" />    <title>Receipt</title>   <style type=""text/css"">body{{  background: none !important; padding: 25px; }} * {{ color: #000 !important;}} </style> </head>      <body>    <div style=""text-align:center"">     <img src =""C:\Projects\VeriBranch.ZiraatBank.AZ\ZiraatDev\Source\Presentation.Web\WebApplication.UI\content\img\newest-ziraat-bank-logo.png""></img>    </div>    <div><form method=""post"" action=""SlipDetail.aspx"" id=""ctl00"">
<div class=""aspNetHidden"">

</div>
<div id=""ExportContentPanel"" class=""lightbox-export"">
	<div><div class=""offrctl"">
   <style type=""text/css"">   .offrctl body,.offrctl table,.offrctl tr,.offrctl td,.offrctl th      .offrctl .middletex   .offrctl .boldtext   .offrctl .mboldtext   .offrctl .lboldtext   .offrctl .bigtext   .offrctl p.small   </style>

   <table width=""100%"" cellpadding=""0"" cellspacing=""0"">
      <tbody>
         <tr>
            <td width=""49%"">&nbsp;          </td>
            <td width=""2%"">&nbsp;          </td>
            <td width=""49%"" align=""right"">
               <table width=""100%"">
                  <tbody>
                     <tr>
                        <td align=""left"" class=""boldtext"">        MƏDAXİL        </td>
                        <td align=""right"" class=""middletex"">VÖEN 4MV3C5M</td>
                     </tr>
                  </tbody>
               </table>
            </td>
         </tr>
      </tbody>
   </table>
   <table width=""100%"">
      <tbody>
         <tr>
            <td width=""49%"" style=""      vertical-align: top;  "">
               <table border=""1"" bordercolor=""black"" width=""100%"" style=""border: 1px solid black; border-collapse: collapse"">
                  <tbody>
                     <tr>
                        <td align=""center"" width=""70%"" class=""bigtext"">         Əməliyyat / Transaction       </td>
                        <td align=""center"" width=""70%"" class=""bigtext"">        Currency        </td>
                     </tr>
                     <tr>
                        <td align=""center"" width=""30%"" class=""boldtext"">         000310920260302142605       </td>
                        <td align=""center"" width=""30%"" class=""boldtext"">        TRY        </td>
                     </tr>
                  </tbody>
               </table>
            </td>
            <td width=""2%"">&nbsp;          </td>
            <td width=""49%"">
               <table border=""1"" bordercolor=""#9C9C9C"" cellpadding=""2"" width=""100%"" style=""border: 1px solid black; border-collapse: collapse"">
                  <tbody>
                     <tr>
                        <td align=""left"" width=""40%"" class=""bigtext"">        Debit / Debit       </td>
                        <td align=""left"" width=""60%"" class=""boldtext"">100,0</td>
                     </tr>
                     <tr>
                        <td align=""left"" width=""40%"" class=""bigtext"">         Kredit / Credit                </td>
                        <td align=""left"" width=""60%"" class=""boldtext"">                </td>
                     </tr>
                  </tbody>
               </table>
            </td>
         </tr>
      </tbody>
   </table>
   <p>&nbsp;</p>
   <table width=""100%"">
      <tbody>
         <tr>
            <td width=""49%"">
               <table border=""0"" cellpadding=""3px"" align=""left"" width=""100%"">
                  <tbody>
                     <tr>
                        <td width=""105"" valign=""top""><strong>Hesab növü</strong> / Type of Acc.</td>
                        <td class=""lboldtext"">Xeyrullayev Tərlan Nazim Oğlu</td>
                     </tr>
                     <tr>
                        <td width=""105"" valign=""top""><strong>İBAN No.</strong></td>
                        <td class=""lboldtext"">AZ29TCZB41020949000310900103</td>
                     </tr>
                     <tr>
                        <td width=""105"" valign=""top""><strong>Cari tarix</strong> / Trans date</td>
                        <td class=""lboldtext"">03.02.2026</td>
                     </tr>
                     <tr>
                        <td width=""105"" valign=""top""><strong>Valör</strong> / Value Date</td>
                        <td class=""lboldtext"">03.02.2026 </td>
                     </tr>
                     <tr>
                        <td width=""105"" valign=""top""><strong>Təyinat</strong>/ Purpose</td>
                        <td class=""lboldtext"">test deneme </td>
                     </tr>
                  </tbody>
               </table>
            </td>
            <td width=""2%"">&nbsp;       </td>
            <td width=""49%"">
               <table border=""0"" cellpadding=""1"" align=""left"" width=""100%"">
                  <tbody>
                     <tr>
                        <td width=""105"" valign=""top""><strong>Alan</strong> / Receiver</td>
                        <td width=""243"" class=""lboldtext""></td>
                     </tr>
                     <tr>
                        <td width=""105"" valign=""top""><strong>Ünvan </strong>/ Address</td>
                        <td width=""243"" class=""lboldtext""></td>
                     </tr>
                     <tr>
                        <td width=""105"" valign=""top""><strong>Təqdim edilib</strong> / Presented</td>
                        <td width=""243"" class=""lboldtext""></td>
                     </tr>
                     <tr>
                        <td colspan=""2"" width=""260""><strong>Yuxaırdakı məbləğ hesabınızda əks olunur</strong>/<br>      Above Amount is booked in your Acc.:</td>
                     </tr>
                     <tr>
                        <td width=""105"" valign=""top""><strong>Məbləğ yazı ilə</strong></td>
                        <td width=""243"" class=""lboldtext"">Sıfır </td>
                     </tr>
                  </tbody>
               </table>
            </td>
         </tr>
      </tbody>
   </table>
   <p><strong>Hörmətlə</strong> / Regards</p>
   <p>&nbsp;</p>
   <p class=""mboldtext"">-</p>
</div></div>
</div>
<div class=""aspNetHidden"">

	
</div></form></div>   </body>  </html>";
    }
}
