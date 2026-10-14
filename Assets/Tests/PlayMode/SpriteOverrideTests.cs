using System.Collections;
using System.Collections.Generic;
using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Facet.PlayTests
{
    /// <summary>
    /// The Phase-0 claim, checked where it actually matters: a Sprite override on the Palette reaches the
    /// views. A view built with an untouched Palette must create no sprite at all (the default look is
    /// untouched), and one built with a ticked override must draw the assigned Sprite while still drawing
    /// its functional overlays in the mesh.
    /// </summary>
    public class SpriteOverrideTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        private Palette _palette;
        private Sprite _sprite;

        [SetUp]
        public void CreatePalette()
        {
            _palette = ScriptableObject.CreateInstance<Palette>();
            _sprite = MakeSprite();
        }

        [TearDown]
        public void DestroyPalette()
        {
            foreach (GameObject go in _created)
                if (go != null) Object.Destroy(go);
            _created.Clear();

            if (_sprite != null)
            {
                if (_sprite.texture != null) Object.Destroy(_sprite.texture);
                Object.Destroy(_sprite);
            }
            if (_palette != null) Object.Destroy(_palette);
        }

        [UnityTest]
        public IEnumerator MachineSpriteOverride_ReplacesTheBody_ButNotTheDefault()
        {
            SimWorld world = SpriteWorld();
            var clock = new FrameClock { Frame = new ViewFrame(1f, 0.02f) };

            // An untouched palette: the built-in look, so no sprite is ever created.
            GameObject plain = Track(new GameObject("Machines"));
            MachineRenderer plainView = plain.AddComponent<MachineRenderer>();
            plainView.Initialize(world, clock, _palette);

            Assert.IsTrue(world.TryPlace(BuildKind.Cannon, new Int2(20, 10), Dir.East));
            yield return null;
            Assert.IsNull(plain.transform.Find("Sprites"), "an untouched palette draws no sprite");

            // A ticked override, on a view that resolves its styles at initialization.
            _palette.Visuals.Machines.Entries = new[]
            {
                new MachineVisuals.Entry
                {
                    Build = BuildKind.Cannon,
                    Style = new VisualStyle
                    {
                        Override = true,
                        Source = VisualSource.Sprite,
                        Sprite = _sprite,
                        Size = 0.5f,
                    },
                },
            };

            GameObject overridden = Track(new GameObject("Machines"));
            MachineRenderer view = overridden.AddComponent<MachineRenderer>();
            view.Initialize(world, clock, _palette);
            yield return null;

            Assert.IsNotNull(FindActiveSprite(overridden, _sprite), "the override sprite is drawn");
            Assert.Greater(overridden.GetComponent<MeshFilter>().sharedMesh.vertexCount, 0,
                "the turret's barrel and ammo overlays are still drawn over the sprite");
        }

        [UnityTest]
        public IEnumerator CoreSpriteOverride_ReplacesTheBlock()
        {
            SimWorld world = SpriteWorld();
            _palette.Visuals.Core = new VisualStyle
            {
                Override = true,
                Source = VisualSource.Sprite,
                Sprite = _sprite,
                Size = 2f,
            };

            var clock = new FrameClock { Frame = new ViewFrame(1f, 0.02f) };
            GameObject go = Track(new GameObject("Core"));
            CoreView view = go.AddComponent<CoreView>();
            view.Initialize(world, clock, _palette);

            yield return null;
            Assert.IsNotNull(FindActiveSprite(go, _sprite), "the Core is drawn as its sprite");
        }

        /// <summary>
        /// A world for a renderer test to draw. Any world will do, so it is made here rather than loaded: a
        /// sprite override is about what a view does with a style, and a test that borrowed a shipped map
        /// would fail for reasons that have nothing to do with sprites. The stockpile is large so that
        /// placing the machine under test is never the thing that fails; the cell it goes on is clear
        /// ground - the Core of a 32x20 map is (14,8)..(17,11).
        /// </summary>
        private static SimWorld SpriteWorld()
            => new SimWorld(new MapDefinition("sprites", 32, 20, 1000,
                new ShapePatch[0], new Int2[0], new WaveDefinition[0]), new SimConfig());

        private GameObject Track(GameObject go)
        {
            _created.Add(go);
            return go;
        }

        private static SpriteRenderer FindActiveSprite(GameObject root, Sprite sprite)
        {
            Transform holder = root.transform.Find("Sprites");
            if (holder == null) return null;

            foreach (Transform child in holder)
            {
                var renderer = child.GetComponent<SpriteRenderer>();
                if (renderer != null && renderer.gameObject.activeSelf && renderer.sprite == sprite)
                    return renderer;
            }

            return null;
        }

        /// <summary>A 4x4 white sprite, so the test needs no imported asset.</summary>
        private static Sprite MakeSprite()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;

            texture.SetPixels(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
        }
    }
}
