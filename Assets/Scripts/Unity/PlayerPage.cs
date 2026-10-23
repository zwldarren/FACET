using System.Runtime.InteropServices;

namespace Facet.Game
{
    /// <summary>
    /// The browser page the player was loaded in, from inside the game: the two things the shell needs
    /// to know about it, and nothing else.
    ///
    /// It exists for one problem. A browser keeps Escape for itself while the page is fullscreen - the
    /// press leaves fullscreen and never reaches the player, so the game's own menu key is dead in the
    /// one mode the game is meant to be played in. The page answers by holding Escape while the frame
    /// is fullscreen (the Keyboard Lock API, Assets/WebGLTemplates/FACET/index.html), which hands the
    /// key back to the game and takes the browser's way out away with it. The way out then has to be
    /// the game's, so the shell offers it: <see cref="InFullscreen"/> draws the pause card's "leave
    /// fullscreen" row and lets Escape leave from the title.
    ///
    /// A build that is not the web player has no page and answers false, which is what keeps the row
    /// and that key out of a desktop build - and out of the EditMode and PlayMode suites, which run in
    /// the Editor. The row is therefore as untestable as the quit button, and for the same reason: it
    /// is a door the host has and the harness does not.
    /// </summary>
    public static class PlayerPage
    {
        /// <summary>Is the page showing the player's frame fullscreen? The one state the shell asks
        /// about, and the one it has to ask again each time it rebuilds: the player can leave fullscreen
        /// with the browser's own keys whatever the game believes.</summary>
        public static bool InFullscreen
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return FacetPageFullscreen() != 0;
#else
                return false;
#endif
            }
        }

        /// <summary>Ask the page to give the window back. The shell only ever asks to leave - it cannot
        /// enter fullscreen, because a browser grants that to a user gesture and the page's own button
        /// already is one. Leaving needs no gesture, which is what lets a row drawn inside the game ask
        /// for it from behind a frame or two and one C# call.</summary>
        public static void LeaveFullscreen()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            FacetPageLeaveFullscreen();
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>The page's own functions (Assets/Plugins/WebGL/FacetPage.jslib), which forward to
        /// the object the template publishes. "__Internal" is how a WebGL build reaches JavaScript the
        /// player links in rather than a library the module owns.</summary>
        [DllImport("__Internal")]
        private static extern int FacetPageFullscreen();

        [DllImport("__Internal")]
        private static extern void FacetPageLeaveFullscreen();
#endif
    }
}
