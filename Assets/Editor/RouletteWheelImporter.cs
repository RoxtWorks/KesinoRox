using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Unity's OBJ importer brings Marcus's EuropeanRoulette_normal.obj in as one mesh, so the
// spinning rotor can't be separated from the fixed bowl. This splits the OBJ by its usemtl
// groups (and each group by radius, rotor vs bowl) into one mesh asset per piece,
// saved under Assets/Resources/RouletteWheel, so the runtime wheel can spin just the rotor parts.
public static class RouletteWheelImporter
{
    const string Source = "Assets/Resources/EuropeanRoulette_normal.obj";
    const string OutDir = "Assets/Resources/RouletteWheel"; // loaded at runtime by RouletteBallWheel
    const float RotorRadius = 3.12f;

    [MenuItem("Tools/Roulette/Import Wheel Model")]
    public static void Import()
    {
        var inv = CultureInfo.InvariantCulture;
        var pos = new List<Vector3>();
        var uvs = new List<Vector2>();
        var nrm = new List<Vector3>();
        var groups = new List<(string name, List<string[]> faces)>();
        List<string[]> current = null;

        foreach (var raw in File.ReadLines(Source))
        {
            var line = raw.Trim();
            if (line.Length < 2) continue;
            var p = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            switch (p[0])
            {
                // OBJ is right-handed; mirror X for Unity (winding is flipped below to match)
                case "v": pos.Add(new Vector3(-float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv))); break;
                case "vt": uvs.Add(new Vector2(float.Parse(p[1], inv), float.Parse(p[2], inv))); break;
                case "vn": nrm.Add(new Vector3(-float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv))); break;
                case "usemtl":
                    current = new List<string[]>();
                    groups.Add((p[1], current));
                    break;
                case "f":
                    if (current == null) { current = new List<string[]>(); groups.Add(("NoMaterial", current)); }
                    var f = new string[p.Length - 1];
                    System.Array.Copy(p, 1, f, 0, f.Length);
                    current.Add(f);
                    break;
            }
        }

        // The rotor (pockets, number ring, frets, cone, turret) spins; the bowl, ball track and
        // rim stay put. Materials that sit on both sides are split by each face's distance from
        // the spindle — the rotor's outer edge is at r ~3.1.
        var split = new List<(string name, List<string[]> faces)>();
        foreach (var (name, faces) in groups)
        {
            var rotor = new List<string[]>();
            var fixedPart = new List<string[]>();
            bool alwaysFixed = name == "MaterialExterior" || name == "MaterialOblicuoExterior";
            foreach (var face in faces)
            {
                Vector3 c = Vector3.zero;
                foreach (var corner in face) c += pos[Resolve(corner.Split('/')[0], pos.Count)];
                c /= face.Length;
                if (!alwaysFixed && new Vector2(c.x, c.z).magnitude < RotorRadius) rotor.Add(face); else fixedPart.Add(face);
            }
            if (rotor.Count > 0) split.Add(("Rotor_" + name, rotor));
            if (fixedPart.Count > 0) split.Add(("Fixed_" + name, fixedPart));
        }

        if (Directory.Exists(OutDir)) AssetDatabase.DeleteAsset(OutDir);
        Directory.CreateDirectory(OutDir);
        var report = new StringBuilder();
        foreach (var (name, faces) in split)
        {
            var map = new Dictionary<string, int>();
            var mv = new List<Vector3>();
            var mu = new List<Vector2>();
            var mn = new List<Vector3>();
            var tris = new List<int>();
            bool hasUv = uvs.Count > 0, hasN = nrm.Count > 0;
            foreach (var face in faces)
            {
                var idx = new int[face.Length];
                for (int i = 0; i < face.Length; i++)
                {
                    if (!map.TryGetValue(face[i], out int vi))
                    {
                        var parts = face[i].Split('/');
                        vi = mv.Count;
                        mv.Add(pos[Resolve(parts[0], pos.Count)]);
                        mu.Add(hasUv && parts.Length > 1 && parts[1] != "" ? uvs[Resolve(parts[1], uvs.Count)] : Vector2.zero);
                        mn.Add(hasN && parts.Length > 2 && parts[2] != "" ? nrm[Resolve(parts[2], nrm.Count)] : Vector3.up);
                        map[face[i]] = vi;
                    }
                    idx[i] = vi;
                }
                // Fan-triangulate, reversed winding because X was mirrored
                for (int i = 1; i + 1 < idx.Length; i++) { tris.Add(idx[0]); tris.Add(idx[i + 1]); tris.Add(idx[i]); }
            }

            var mesh = new Mesh { name = name, indexFormat = mv.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(mv);
            mesh.SetUVs(0, mu);
            mesh.SetTriangles(tris, 0);
            if (hasN) mesh.SetNormals(mn); else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, $"{OutDir}/{name}.asset");

            float rMin = float.MaxValue, rMax = 0f, yMin = float.MaxValue, yMax = float.MinValue;
            foreach (var v in mv)
            {
                float r = new Vector2(v.x, v.z).magnitude;
                rMin = Mathf.Min(rMin, r); rMax = Mathf.Max(rMax, r);
                yMin = Mathf.Min(yMin, v.y); yMax = Mathf.Max(yMax, v.y);
            }
            report.AppendLine($"{name}: verts={mv.Count} tris={tris.Count / 3} r={rMin:F3}..{rMax:F3} y={yMin:F3}..{yMax:F3}");
        }
        AssetDatabase.SaveAssets();
        Debug.Log("RouletteWheelImporter\n" + report);
    }

    static int Resolve(string s, int count)
    {
        int i = int.Parse(s, CultureInfo.InvariantCulture);
        return i > 0 ? i - 1 : count + i;
    }
}
