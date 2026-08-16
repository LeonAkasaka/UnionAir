using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// One field of each serialized type the component read used to drop.
    /// </summary>
    /// <remarks>
    /// Its own file, and named for the class, for the reason
    /// <see cref="UnionAirPropertyKeyFixture"/> is: Unity resolves the script a serialized object
    /// uses by that pairing, and a fixture sharing a file with its tests becomes a missing script
    /// after a domain reload.
    ///
    /// A ScriptableObject rather than a MonoBehaviour, because a MonoBehaviour declared in an
    /// Editor-only assembly cannot be attached to a GameObject -- measured, <c>AddComponent</c>
    /// adds nothing and the read reports only the Transform. The types are therefore exercised
    /// through the shared serializer directly, and the component read is shown to reach it by a
    /// Transform's own <c>Quaternion</c>.
    /// </remarks>
    internal sealed class UnionAirSerializedTypeFixture : ScriptableObject
    {
        public Quaternion rotation = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);
        public Bounds volume = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(2f, 4f, 6f));
        public Rect area = new Rect(1f, 2f, 3f, 4f);
    }
}
