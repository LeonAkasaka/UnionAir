using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// A ScriptableObject with one field of each shape the write path treats differently.
    /// </summary>
    /// <remarks>
    /// Its own file, and named for the class, because Unity resolves the script a serialized
    /// asset instantiates by that pairing; a fixture sharing a file with its tests loads today
    /// and becomes a missing script after a domain reload.
    /// </remarks>
    internal sealed class UnionAirPropertyKeyFixture : ScriptableObject
    {
        public string displayName = "start";
        public float cooldown = 1f;
        public string[] tags = new string[0];
    }
}
