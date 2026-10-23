using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using Facet.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Facet.Game
{
    /// <summary>
    /// The data collection half of the matched-round analytics pipeline: once a frame it lets a
    /// <see cref="MatchRateTracker"/> read the driver's world, and every finished wave becomes one form
    /// submission to the Google Form in <see cref="AnalyticsConfig"/>.
    ///
    /// A view-side reader like every other: it reads the world after the frame's ticks and never writes to
    /// it, so a run plays identically with analytics on or off. In the WebGL player the submission goes out
    /// through <c>Assets/Plugins/WebGL/FacetAnalytics.jslib</c> as a no-cors <c>fetch</c> - a browser will
    /// not let a page read Google's reply across origins, but it does deliver the request, which is all a
    /// form submission needs. Elsewhere it is a <see cref="UnityWebRequest"/>, or, in the Editor by
    /// default, a Console line.
    /// </summary>
    public sealed class AnalyticsReporter : MonoBehaviour
    {
        private SimulationDriver _driver;
        private readonly MatchRateTracker _tracker = new MatchRateTracker();

        /// <summary>One id per page load, so the rows of one player's session can be told apart from
        /// another's without knowing anything about who they are.</summary>
        private string _session;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void FacetAnalyticsPost(string url, string body);
#endif

        public void Bind(SimulationDriver driver)
        {
            _driver = driver;
            if (_session == null) _session = Guid.NewGuid().ToString("N").Substring(0, 10);
        }

        private void LateUpdate()
        {
            if (_driver == null || _driver.World == null) return;
            if (_tracker.Poll(_driver.World, out WaveMatchRow row)) Report(row);
        }

        private void Report(WaveMatchRow row)
        {
            if (!AnalyticsConfig.Configured) return;

            string body = Body(row);

#if UNITY_WEBGL && !UNITY_EDITOR
            FacetAnalyticsPost(AnalyticsConfig.FormResponseUrl, body);
#else
            if (Application.isEditor && !AnalyticsConfig.SendFromEditor)
            {
                Debug.Log("FACET analytics (not sent from the Editor): " + row + " rate " +
                    row.MatchRate.ToString("P0"), this);
                return;
            }

            StartCoroutine(Post(body));
#endif
        }

        /// <summary>The form submission, url-encoded: one <c>entry.NNN=value</c> per question.</summary>
        private string Body(WaveMatchRow row)
        {
            var body = new StringBuilder();
            Append(body, AnalyticsConfig.EntrySession, _session);
            Append(body, AnalyticsConfig.EntryMap, row.Map);
            Append(body, AnalyticsConfig.EntryWave, row.Wave.ToString());
            Append(body, AnalyticsConfig.EntryMatched, row.Matched.ToString());
            Append(body, AnalyticsConfig.EntryResisted, row.Resisted.ToString());
            Append(body, AnalyticsConfig.EntryOutcome, row.Outcome);
            Append(body, AnalyticsConfig.EntryBuild, AnalyticsConfig.BuildLabel);
            return body.ToString();
        }

        private static void Append(StringBuilder body, string entry, string value)
        {
            if (body.Length > 0) body.Append('&');
            body.Append(entry).Append('=').Append(UnityWebRequest.EscapeURL(value ?? string.Empty));
        }

        private static IEnumerator Post(string body)
        {
            using (var request = new UnityWebRequest(AnalyticsConfig.FormResponseUrl, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                    Debug.LogWarning("FACET analytics: the form submission failed - " + request.error);
            }
        }
    }
}
