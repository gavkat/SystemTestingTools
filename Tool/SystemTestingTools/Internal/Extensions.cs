﻿using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using static SystemTestingTools.Internal.Enums;

namespace SystemTestingTools.Internal
{
    internal static class Extensions
    {
        public static string GetHeaderValue(this HttpHeaders headers, string key, string separator)
        {
            if (!headers.Contains(key)) return null;

            return string.Join(separator, headers.GetValues(key));
        }

        public static async Task<HttpResponseMessage> Clone(this HttpResponseMessage response)
        {
            // copied from https://stackoverflow.com/questions/25044166/how-to-clone-a-httprequestmessage-when-the-original-request-has-content/34049029

            var newResponse = new HttpResponseMessage(response.StatusCode);
            var ms = new MemoryStream();

            foreach (var v in response.Headers) newResponse.Headers.TryAddWithoutValidation(v.Key, v.Value);
            if (response.Content != null)
            {
                await response.Content.CopyToAsync(ms).ConfigureAwait(false);
                ms.Position = 0;
                newResponse.Content = new StreamContent(ms);

                foreach (var v in response.Content.Headers) newResponse.Content.Headers.TryAddWithoutValidation(v.Key, v.Value);

            }
            return newResponse;
        }

        internal static string SeparatedByComa(this IEnumerable<string> values)
        {
            return string.Join(",", values);
        }

        internal static void ApppendHeaders<T1, T2>(this Dictionary<T1, T2> currentDic, Dictionary<T1, T2> otherDic)
        {
            foreach (var item in otherDic)
                currentDic.Add(item.Key, item.Value);
        }

        internal static string GetContentType(this KnownContentTypes contentType)
        {
            switch (contentType)
            {
                case KnownContentTypes.Json:
                    return "application/json";
                case KnownContentTypes.Xml:
                    return "application/xml";
            }
            return "text/plain";
        }
    }
}
