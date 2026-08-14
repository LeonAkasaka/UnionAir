using System;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// A ScriptableObject with one field of each shape the write path treats differently.
    /// </summary>
    /// <remarks>
    /// Its own file, and named for the class, because Unity resolves the script a serialized
    /// asset instantiates by that pairing; a fixture sharing a file with its tests loads today
    /// and becomes a missing script after a domain reload. The nested entry type is not a
    /// ScriptableObject and carries no such pairing, so it stays here beside the field using it.
    /// </remarks>
    internal sealed class UnionAirPropertyKeyFixture : ScriptableObject
    {
        /// <summary>An element the read direction cannot serialize, so the write must refuse it.</summary>
        [Serializable]
        internal struct Entry
        {
            public int hp;
        }

        public string displayName = "start";
        public float cooldown = 1f;
        public Vector3 offset = new Vector3(1f, 2f, 3f);
        public UnityEngine.Object reference;
        public string[] tags = new string[0];
        public UnityEngine.Object[] references = new UnityEngine.Object[0];

        // One holding an element and one empty, because an array of unwritable elements has to be
        // refused both for what it holds now and for what a resize would give it.
        public Entry[] entries = { new Entry { hp = 1 } };
        public Entry[] spares = new Entry[0];
    }
}
