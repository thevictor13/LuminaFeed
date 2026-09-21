using System.Text.Encodings.Web;

namespace LuminaFeed.Services.Email;

/// <summary>
/// Table-based, Outlook-safe HTML email layout. Word-based Outlook clients can't render div-based
/// layouts reliably, so the shell uses tables and inline styles. Buttons are emitted as an
/// MSO-conditional VML "bulletproof" button for Outlook alongside a normal anchor for every other
/// client — the two are mutually exclusive, so the Outlook-only duplicate never shows elsewhere.
/// </summary>
/// <remarks>
/// Text nodes and URLs passed here are HTML-encoded so untrusted values (e.g. future article titles
/// and links) cannot inject markup. <c>bodyHtml</c> is treated as trusted HTML — callers must pass
/// already-safe markup.
/// </remarks>
public static class EmailLayout
{
    private const string BrandColor = "#0d6efd";

    /// <summary>Wraps <paramref name="bodyHtml"/> (trusted HTML) in the centered, table-based email shell.</summary>
    public static string Render(string heading, string bodyHtml)
    {
        var encodedHeading = HtmlEncoder.Default.Encode(heading);
        return
$$"""
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <meta http-equiv="X-UA-Compatible" content="IE=edge" />
  <!--[if mso]><style type="text/css">table, td { font-family: Arial, sans-serif; }</style><![endif]-->
  <title>{{encodedHeading}}</title>
</head>
<body style="margin:0; padding:0; background-color:#f4f4f5;">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:#f4f4f5;">
    <tr>
      <td align="center" style="padding:24px 12px;">
        <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" style="width:600px; max-width:600px; background-color:#ffffff; border-radius:8px;">
          <tr>
            <td style="padding:32px 32px 8px 32px; font-family:Arial, sans-serif; font-size:22px; font-weight:bold; color:#111111;">{{encodedHeading}}</td>
          </tr>
          <tr>
            <td style="padding:8px 32px 32px 32px; font-family:Arial, sans-serif; font-size:15px; line-height:22px; color:#333333;">{{bodyHtml}}</td>
          </tr>
        </table>
        <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" style="width:600px; max-width:600px;">
          <tr>
            <td style="padding:16px 32px; font-family:Arial, sans-serif; font-size:12px; color:#999999;">LuminaFeed</td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>
""";
    }

    /// <summary>
    /// A "bulletproof" call-to-action button. Outlook renders the VML rounded rectangle; every other
    /// client renders the anchor. <paramref name="text"/> and <paramref name="url"/> are HTML-encoded.
    /// </summary>
    public static string Button(string text, string url)
    {
        var encodedText = HtmlEncoder.Default.Encode(text);
        var encodedUrl = HtmlEncoder.Default.Encode(url);
        return
$$"""
<table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:16px 0;">
  <tr>
    <td align="center">
      <!--[if mso]>
      <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{encodedUrl}}" style="height:44px; v-text-anchor:middle; width:220px;" arcsize="12%" strokecolor="{{BrandColor}}" fillcolor="{{BrandColor}}">
        <w:anchorlock/>
        <center style="color:#ffffff; font-family:Arial, sans-serif; font-size:16px; font-weight:bold;">{{encodedText}}</center>
      </v:roundrect>
      <![endif]-->
      <!--[if !mso]><!-->
      <a href="{{encodedUrl}}" style="display:inline-block; background-color:{{BrandColor}}; color:#ffffff; font-family:Arial, sans-serif; font-size:16px; font-weight:bold; text-decoration:none; padding:12px 24px; border-radius:6px;">{{encodedText}}</a>
      <!--<![endif]-->
    </td>
  </tr>
</table>
""";
    }
}
