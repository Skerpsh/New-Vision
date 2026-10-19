// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Link preview like in messengers: follows redirects one by one (so the real final site is known), reads the
    /// beginning of the page and takes its title, description and image (Open Graph / Twitter tags, &lt;title&gt;).
    /// </summary>
    public static class LinkPreview
    {
        public class Data
        {
            public Uri FinalUrl;
            public string Title;
            public string Description;
            public string SiteName;
            public Texture2D Image;
            public string Error;
        }

        private const int MaxRedirects = 6;
        private const int MaxPageBytes = 512 * 1024;
        private const int MaxImageBytes = 3 * 1024 * 1024;
        private const int TimeoutSeconds = 10;
        private const string UserAgent = "Mozilla/5.0 (Linux; Android 14; Quest 3) AppleWebKit/537.36 (KHTML, like Gecko) " +
                                         "Chrome/126.0 Mobile Safari/537.36 NewVisionLinkPreview/1.0";

        /// <summary>Calls <paramref name="onFinalUrl"/> as soon as redirects are resolved, then <paramref name="done"/> once.</summary>
        public static IEnumerator Fetch(Uri url, Action<Uri> onFinalUrl, Action<Data> done)
        {
            var data = new Data { FinalUrl = url };
            string html = null;
            string contentType = null;

            for (var hop = 0; ; hop++)
            {
                using var request = new UnityWebRequest(data.FinalUrl, UnityWebRequest.kHttpVerbGET,
                    new LimitedDownloadHandler(MaxPageBytes), null);
                request.redirectLimit = 0;
                request.timeout = TimeoutSeconds;
                request.SetRequestHeader("User-Agent", UserAgent);
                request.SetRequestHeader("Accept", "text/html,application/xhtml+xml;q=0.9,*/*;q=0.5");
                yield return request.SendWebRequest();

                var code = request.responseCode;
                if (code is >= 300 and < 400)
                {
                    var location = request.GetResponseHeader("Location");
                    if (string.IsNullOrEmpty(location) || hop >= MaxRedirects ||
                        !Uri.TryCreate(data.FinalUrl, location, out var next) ||
                        (next.Scheme != Uri.UriSchemeHttps && next.Scheme != Uri.UriSchemeHttp))
                    {
                        data.Error = hop >= MaxRedirects ? "Too many redirects" : "Redirects to a non-web address";
                        break;
                    }
                    data.FinalUrl = next;
                    continue;
                }

                var handler = (LimitedDownloadHandler)request.downloadHandler;
                // The handler stops reading after MaxPageBytes on purpose, which Unity reports as an error: the start of the page is enough
                if (handler.Length == 0)
                {
                    data.Error = code > 0 ? $"Site answered {code}" : request.error ?? "No connection";
                    break;
                }
                if (code >= 400)
                {
                    data.Error = $"Site answered {code}";
                }
                contentType = request.GetResponseHeader("Content-Type") ?? "";
                if (contentType.Contains("html") || contentType.Length == 0)
                {
                    html = handler.Text;
                }
                break;
            }
            onFinalUrl?.Invoke(data.FinalUrl);

            if (html != null)
            {
                data.Title = MetaTag(html, "og:title") ?? MetaTag(html, "twitter:title") ?? TitleTag(html);
                data.Description = MetaTag(html, "og:description") ?? MetaTag(html, "twitter:description") ?? MetaTag(html, "description");
                data.SiteName = MetaTag(html, "og:site_name");
                var image = MetaTag(html, "og:image") ?? MetaTag(html, "og:image:url") ?? MetaTag(html, "twitter:image");
                if (image != null && Uri.TryCreate(data.FinalUrl, image, out var imageUrl) &&
                    (imageUrl.Scheme == Uri.UriSchemeHttps || imageUrl.Scheme == Uri.UriSchemeHttp))
                {
                    yield return LoadImage(imageUrl, texture => data.Image = texture);
                }
            }
            else if (data.Error == null && contentType != null)
            {
                data.Description = $"Not a web page ({contentType.Split(';')[0].Trim()})";
            }
            done(data);
        }

        private static IEnumerator LoadImage(Uri url, Action<Texture2D> done)
        {
            using var request = UnityWebRequestTexture.GetTexture(url, true);
            request.timeout = TimeoutSeconds;
            request.SetRequestHeader("User-Agent", UserAgent);
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (request.downloadedBytes > MaxImageBytes)
                {
                    request.Abort();
                    yield break;
                }
                yield return null;
            }
            if (request.result == UnityWebRequest.Result.Success)
            {
                done(DownloadHandlerTexture.GetContent(request));
            }
        }

        private static string MetaTag(string html, string name)
        {
            // <meta property="og:title" content="..."> with the attributes in either order, ' or " quotes
            var n = Regex.Escape(name);
            var match = Regex.Match(html,
                $@"<meta[^>]+(?:property|name)\s*=\s*[""']{n}[""'][^>]*?content\s*=\s*(?:""([^""]*)""|'([^']*)')",
                RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                match = Regex.Match(html,
                    $@"<meta[^>]+content\s*=\s*(?:""([^""]*)""|'([^']*)')[^>]*?(?:property|name)\s*=\s*[""']{n}[""']",
                    RegexOptions.IgnoreCase);
            }
            return match.Success ? Clean(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) : null;
        }

        private static string TitleTag(string html)
        {
            var match = Regex.Match(html, @"<title[^>]*>([^<]*)</title>", RegexOptions.IgnoreCase);
            return match.Success ? Clean(match.Groups[1].Value) : null;
        }

        private static string Clean(string text)
        {
            text = Regex.Replace(WebUtility.HtmlDecode(text), @"\s+", " ").Trim();
            return text.Length == 0 ? null : text.Length > 300 ? text.Substring(0, 300) + "..." : text;
        }

        /// <summary>Keeps at most maxBytes of the body and then stops the download.</summary>
        private class LimitedDownloadHandler : DownloadHandlerScript
        {
            private readonly int m_maxBytes;
            private readonly System.IO.MemoryStream m_data = new();

            public LimitedDownloadHandler(int maxBytes) : base(new byte[16 * 1024]) => m_maxBytes = maxBytes;

            public int Length => (int)m_data.Length;
            public string Text => Encoding.UTF8.GetString(m_data.GetBuffer(), 0, (int)m_data.Length);

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                var take = Mathf.Min(dataLength, m_maxBytes - (int)m_data.Length);
                if (take > 0)
                {
                    m_data.Write(data, 0, take);
                }
                return m_data.Length < m_maxBytes;
            }
        }
    }
}
