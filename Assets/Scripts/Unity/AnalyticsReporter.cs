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
    /// The data collection half of the analytics pipeline: once a frame it lets a
    /// <see cref="MatchRateTracker"/> and a <see cref="StoppageTracker"/> read the driver's world, and every
    /// finished wave becomes one form submission per metric - matched rounds to one Google Form, line
    /// stoppages to another - both named in <see cref="AnalyticsConfig"/>.
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
        private readonly StoppageTracker _stoppages = new StoppageTracker();

        /// <summary>One id per page load, so the rows of one player's session can be told apart from
        /// another's without knowing anything about who they are.</summary>
        private string _session;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void FacetAnalyticsPost(string url, string body);
#endif

        /// <summary>Point the reporter at the world it reads, once, from the driver's own <c>Awake</c>. The
        /// session id is made here rather than in <c>Awake</c> so that binding again - a reload, a second
        /// call - keeps the run's rows under one id instead of splitting one player's session in two.</summary>
        public void Bind(SimulationDriver driver)
        {
            _driver = driver;
            if (_session == null) _session = Guid.NewGuid().ToString("N").Substring(0, 10);
        }

        private void LateUpdate()
        {
            if (_driver == null || _driver.World == null) return;
            if (_tracker.Poll(_driver.World, out WaveMatchRow row)) Report(row);
            if (_stoppages.Poll(_driver.World, out WaveStoppageRow stops)) Report(stops);
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

            StartCoroutine(Post(AnalyticsConfig.FormResponseUrl, body));
#endif
        }

        private void Report(WaveStoppageRow row)
        {
            if (!AnalyticsConfig.StoppagesConfigured) return;

            var body = new StringBuilder();
            Append(body, AnalyticsConfig.EntryStopSession, _session);
            Append(body, AnalyticsConfig.EntryStopMap, row.Map);
            Append(body, AnalyticsConfig.EntryStopWave, row.Wave.ToString());
            Append(body, AnalyticsConfig.EntryStopJammed, row.Jammed.ToString());
            Append(body, AnalyticsConfig.EntryStopBlocked, row.Blocked.ToString());
            Append(body, AnalyticsConfig.EntryStopStalled, row.Stalled.ToString());
            Append(body, AnalyticsConfig.EntryStopOutcome, row.Outcome);
            Append(body, AnalyticsConfig.EntryStopBuild, AnalyticsConfig.BuildLabel);

#if UNITY_WEBGL && !UNITY_EDITOR
            FacetAnalyticsPost(AnalyticsConfig.StoppageFormResponseUrl, body.ToString());
#else
            if (Application.isEditor && !AnalyticsConfig.SendFromEditor)
            {
                Debug.Log("FACET analytics (not sent from the Editor): " + row, this);
                return;
            }

            StartCoroutine(Post(AnalyticsConfig.StoppageFormResponseUrl, body.ToString()));
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

        private static IEnumerator Post(string url, string body)
        {
            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
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
