// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Text;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>What a scanned QR code / barcode contains, parsed into something the app can act on.</summary>
    public class CodeContent
    {
        public enum ContentKind { Url, Wifi, Product, Phone, Email, Contact, Text }

        public ContentKind Kind;
        /// <summary>Raw decoded text.</summary>
        public string Raw;
        /// <summary>ZXing format name, e.g. QR_CODE, EAN_13.</summary>
        public string Format;

        public Uri Url;
        public string WifiSsid;
        public string WifiPassword;
        /// <summary>WPA, WEP, SAE (WPA3) or empty for an open network.</summary>
        public string WifiSecurity;
        public bool WifiHidden;

        private static readonly HashSet<string> s_productFormats = new() { "EAN_13", "EAN_8", "UPC_A", "UPC_E" };

        /// <summary>Short text for the hint under the reticle.</summary>
        public string ChipText => Kind switch
        {
            ContentKind.Url => $"Link  ·  {Url.Host}",
            ContentKind.Wifi => $"Wi-Fi  ·  {WifiSsid}",
            ContentKind.Product => $"Barcode  ·  {Raw}",
            ContentKind.Phone => "Phone number",
            ContentKind.Email => "E-mail",
            ContentKind.Contact => "Contact card",
            _ => Format == "QR_CODE" ? "QR  ·  text" : "Code  ·  text"
        };

        public static CodeContent Parse(string text, string format)
        {
            var content = new CodeContent { Raw = text.Trim(), Format = format, Kind = ContentKind.Text };
            var raw = content.Raw;

            if (s_productFormats.Contains(format))
            {
                content.Kind = ContentKind.Product;
            }
            else if (raw.StartsWith("WIFI:", StringComparison.OrdinalIgnoreCase))
            {
                ParseWifi(raw.Substring(5), content);
            }
            else if (TryParseWebUrl(raw, out var url))
            {
                content.Kind = ContentKind.Url;
                content.Url = url;
            }
            else if (raw.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
            {
                content.Kind = ContentKind.Phone;
            }
            else if (raw.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("MATMSG:", StringComparison.OrdinalIgnoreCase))
            {
                content.Kind = ContentKind.Email;
            }
            else if (raw.StartsWith("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("MECARD:", StringComparison.OrdinalIgnoreCase))
            {
                content.Kind = ContentKind.Contact;
            }
            return content;
        }

        /// <summary>Only http(s) links count as links; "www.site.com" without a scheme gets https.</summary>
        public static bool TryParseWebUrl(string text, out Uri url)
        {
            url = null;
            if (string.IsNullOrWhiteSpace(text) || text.Contains(" "))
            {
                return false;
            }
            if (text.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            {
                text = "https://" + text;
            }
            return Uri.TryCreate(text, UriKind.Absolute, out url) &&
                   (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp) &&
                   !string.IsNullOrEmpty(url.Host);
        }

        /// <summary>WIFI:S:name;T:WPA;P:password;H:false;; — fields in any order, \ escapes ; , : " and \.</summary>
        private static void ParseWifi(string body, CodeContent content)
        {
            content.Kind = ContentKind.Wifi;
            content.WifiSecurity = "";
            var field = new StringBuilder();
            var escaped = false;
            var fields = new List<string>();
            foreach (var c in body)
            {
                if (escaped)
                {
                    _ = field.Append(c);
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == ';')
                {
                    fields.Add(field.ToString());
                    _ = field.Clear();
                }
                else
                {
                    _ = field.Append(c);
                }
            }
            if (field.Length > 0)
            {
                fields.Add(field.ToString());
            }

            foreach (var f in fields)
            {
                var colon = f.IndexOf(':');
                if (colon < 1)
                {
                    continue;
                }
                var key = f.Substring(0, colon).ToUpperInvariant();
                var value = f.Substring(colon + 1);
                switch (key)
                {
                    case "S":
                        content.WifiSsid = value;
                        break;
                    case "P":
                        content.WifiPassword = value;
                        break;
                    case "T":
                        var security = value.ToUpperInvariant();
                        content.WifiSecurity = security == "NOPASS" ? "" : security;
                        break;
                    case "H":
                        content.WifiHidden = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;
                }
            }
            content.WifiSsid ??= "";
        }

        /// <summary>Plain-language warnings about a link, empty when nothing looks off.</summary>
        public static List<string> LinkWarnings(Uri url, Uri finalUrl)
        {
            var warnings = new List<string>();
            var target = finalUrl ?? url;
            if (finalUrl != null && !string.Equals(WithoutWww(finalUrl.Host), WithoutWww(url.Host), StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Redirects to another site: {finalUrl.Host}");
            }
            if (target.Scheme == Uri.UriSchemeHttp)
            {
                warnings.Add("Not encrypted (http)");
            }
            if (target.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
            {
                warnings.Add("Address is a bare IP, not a domain name");
            }
            if (target.Host.Contains("xn--") || target.IdnHost != target.Host)
            {
                warnings.Add("Domain uses look-alike letters, check the spelling");
            }
            if (!string.IsNullOrEmpty(target.UserInfo))
            {
                warnings.Add("Link hides the real address behind '@'");
            }
            var path = target.AbsolutePath.ToLowerInvariant();
            foreach (var ext in new[] { ".apk", ".exe", ".msi", ".bat", ".cmd", ".scr", ".dmg", ".pkg", ".zip", ".rar", ".7z", ".js", ".vbs" })
            {
                if (path.EndsWith(ext))
                {
                    warnings.Add($"Downloads a file ({ext})");
                    break;
                }
            }
            return warnings;
        }

        private static string WithoutWww(string host) =>
            host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host.Substring(4) : host;
    }
}
