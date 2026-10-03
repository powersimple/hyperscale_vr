// WGS84 conversions in double precision, and the small meshes the layers draw.
// Layers keep their geometry in Earth-centered, Earth-fixed (ECEF) meters; the globe rig
// supplies one matrix per frame that carries ECEF into the Unity world.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AtlasVR
{
    public struct D3
    {
        public double x, y, z;
        public D3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public static D3 operator +(D3 a, D3 b) { return new D3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static D3 operator -(D3 a, D3 b) { return new D3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static D3 operator *(D3 a, double k) { return new D3(a.x * k, a.y * k, a.z * k); }
        public double Length { get { return Math.Sqrt(x * x + y * y + z * z); } }
        public D3 Normalized { get { double l = Length; return l > 0 ? this * (1.0 / l) : this; } }
        public static double Dot(D3 a, D3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
        public static D3 Cross(D3 a, D3 b) { return new D3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
        public Vector3 ToVector3() { return new Vector3((float)x, (float)y, (float)z); }
    }

    public static class Wgs84
    {
        public const double A = 6378137.0;
        public const double B = 6356752.314245;   // polar radius
        const double F = 1.0 / 298.257223563;
        const double E2 = F * (2 - F);
        const double Deg = Math.PI / 180.0;

        public static D3 ToEcef(double lonDeg, double latDeg, double h)
        {
            double lat = latDeg * Deg, lon = lonDeg * Deg;
            double sl = Math.Sin(lat), cl = Math.Cos(lat);
            double n = A / Math.Sqrt(1 - E2 * sl * sl);
            return new D3((n + h) * cl * Math.Cos(lon), (n + h) * cl * Math.Sin(lon), (n * (1 - E2) + h) * sl);
        }

        public static D3 Up(double lonDeg, double latDeg)
        {
            double lat = latDeg * Deg, lon = lonDeg * Deg;
            return new D3(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
        }
        public static D3 East(double lonDeg) { double lon = lonDeg * Deg; return new D3(-Math.Sin(lon), Math.Cos(lon), 0); }
        public static D3 North(double lonDeg, double latDeg)
        {
            double lat = latDeg * Deg, lon = lonDeg * Deg;
            return new D3(-Math.Sin(lat) * Math.Cos(lon), -Math.Sin(lat) * Math.Sin(lon), Math.Cos(lat));
        }

        /// Instance matrix for a flat marker: columns east, up, north (scaled by k), translation
        /// the ECEF position rounded to float ("high"); the remainder ("low") goes to the shader
        /// separately so positions stay exact to the centimeter next to the viewer.
        /// The marker meshes lie in their local XZ plane with +Y up.
        public static Matrix4x4 Marker(double lonDeg, double latDeg, double h, float k, out Vector4 low)
        {
            D3 p = ToEcef(lonDeg, latDeg, h);
            Vector3 hi = p.ToVector3();
            low = new Vector4((float)(p.x - hi.x), (float)(p.y - hi.y), (float)(p.z - hi.z), 0);
            Vector3 e = East(lonDeg).ToVector3() * k, u = Up(lonDeg, latDeg).ToVector3() * k, n = North(lonDeg, latDeg).ToVector3() * k;
            var m = new Matrix4x4();
            m.SetColumn(0, new Vector4(e.x, e.y, e.z, 0));
            m.SetColumn(1, new Vector4(u.x, u.y, u.z, 0));
            m.SetColumn(2, new Vector4(n.x, n.y, n.z, 0));
            m.SetColumn(3, new Vector4(hi.x, hi.y, hi.z, 1));
            return m;
        }

        /// Splits an ECEF position into a float part and the float remainder.
        public static void Split(D3 p, out Vector3 hi, out Vector3 lo)
        {
            hi = p.ToVector3();
            lo = new Vector3((float)(p.x - hi.x), (float)(p.y - hi.y), (float)(p.z - hi.z));
        }

        /// Great-circle interpolation between two lon/lat points (degrees), t in [0,1].
        public static void Slerp(double lon0, double lat0, double lon1, double lat1, double t, out double lon, out double lat)
        {
            D3 a = Up(lon0, lat0), b = Up(lon1, lat1);
            double d = Math.Acos(Math.Max(-1, Math.Min(1, D3.Dot(a, b))));
            if (d < 1e-9) { lon = lon0; lat = lat0; return; }
            double s = Math.Sin(d);
            D3 p = a * (Math.Sin((1 - t) * d) / s) + b * (Math.Sin(t * d) / s);
            lat = Math.Asin(Math.Max(-1, Math.Min(1, p.z))) / Deg;
            lon = Math.Atan2(p.y, p.x) / Deg;
        }

        /// The point a given distance (m) along a bearing (degrees from north) from a start point.
        public static void Destination(double lonDeg, double latDeg, double bearingDeg, double meters, out double lon, out double lat)
        {
            double d = meters / A, b = bearingDeg * Deg, la = latDeg * Deg, lo = lonDeg * Deg;
            double la2 = Math.Asin(Math.Sin(la) * Math.Cos(d) + Math.Cos(la) * Math.Sin(d) * Math.Cos(b));
            double lo2 = lo + Math.Atan2(Math.Sin(b) * Math.Sin(d) * Math.Cos(la), Math.Cos(d) - Math.Sin(la) * Math.Sin(la2));
            lat = la2 / Deg;
            lon = ((lo2 / Deg + 540) % 360) - 180;
        }

        /// Initial bearing (degrees from north) from one point to another.
        public static double Bearing(double lon0, double lat0, double lon1, double lat1)
        {
            double la0 = lat0 * Deg, la1 = lat1 * Deg, dlo = (lon1 - lon0) * Deg;
            double y = Math.Sin(dlo) * Math.Cos(la1), x = Math.Cos(la0) * Math.Sin(la1) - Math.Sin(la0) * Math.Cos(la1) * Math.Cos(dlo);
            return (Math.Atan2(y, x) / Deg + 360) % 360;
        }

        public static double AngleDeg(double lon0, double lat0, double lon1, double lat1)
        {
            return Math.Acos(Math.Max(-1, Math.Min(1, D3.Dot(Up(lon0, lat0), Up(lon1, lat1))))) / Deg;
        }
    }

    public static class Meshes
    {
        /// Flat disc of radius 0.5 in the XZ plane, facing +Y.
        public static Mesh Disc(int segments = 24)
        {
            var v = new Vector3[segments + 1];
            var t = new int[segments * 3];
            v[0] = Vector3.zero;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                v[i + 1] = new Vector3(Mathf.Cos(a) * 0.5f, 0, Mathf.Sin(a) * 0.5f);
                t[i * 3] = 0; t[i * 3 + 1] = 1 + (i + 1) % segments; t[i * 3 + 2] = 1 + i;
            }
            var m = new Mesh { name = "AtlasDisc", vertices = v, triangles = t };
            m.RecalculateBounds();
            return m;
        }

        /// Ring between radii inner and 0.5 in the XZ plane, facing +Y.
        public static Mesh Ring(float inner, int segments = 32)
        {
            var v = new Vector3[segments * 2];
            var t = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                v[i * 2] = new Vector3(c * inner, 0, s * inner);
                v[i * 2 + 1] = new Vector3(c * 0.5f, 0, s * 0.5f);
                int j = (i + 1) % segments;
                int o = i * 6;
                t[o] = i * 2; t[o + 1] = j * 2; t[o + 2] = i * 2 + 1;
                t[o + 3] = i * 2 + 1; t[o + 4] = j * 2; t[o + 5] = j * 2 + 1;
            }
            var m = new Mesh { name = "AtlasRing", vertices = v, triangles = t };
            m.RecalculateBounds();
            return m;
        }

        /// A panel backing: a box behind the rect (x0..x1, y0..y1) from z0 back to z0 + depth, its
        /// front edge chamfered by bevel so the rim catches the light. The front faces -z (the viewer).
        public static Mesh Slab(float x0, float y0, float x1, float y1, float z0, float depth, float bevel)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var n = new List<Vector3>();
            float b = Mathf.Min(bevel, Mathf.Min(x1 - x0, y1 - y0) * 0.25f), zb = z0 + b, zk = z0 + depth;
            Vector3 f00 = new Vector3(x0 + b, y0 + b, z0), f10 = new Vector3(x1 - b, y0 + b, z0), f11 = new Vector3(x1 - b, y1 - b, z0), f01 = new Vector3(x0 + b, y1 - b, z0);
            Vector3 r00 = new Vector3(x0, y0, zb), r10 = new Vector3(x1, y0, zb), r11 = new Vector3(x1, y1, zb), r01 = new Vector3(x0, y1, zb);
            Vector3 k00 = new Vector3(x0, y0, zk), k10 = new Vector3(x1, y0, zk), k11 = new Vector3(x1, y1, zk), k01 = new Vector3(x0, y1, zk);
            Quad(v, t, n, f00, f10, f11, f01, Vector3.back);                         // front
            Quad(v, t, n, f01, f11, r11, r01, new Vector3(0, 1, -1).normalized);     // bevels
            Quad(v, t, n, f00, r00, r10, f10, new Vector3(0, -1, -1).normalized);
            Quad(v, t, n, f00, f01, r01, r00, new Vector3(-1, 0, -1).normalized);
            Quad(v, t, n, f10, r10, r11, f11, new Vector3(1, 0, -1).normalized);
            Quad(v, t, n, r01, r11, k11, k01, Vector3.up);                           // sides
            Quad(v, t, n, r00, k00, k10, r10, Vector3.down);
            Quad(v, t, n, r00, r01, k01, k00, Vector3.left);
            Quad(v, t, n, r10, k10, k11, r11, Vector3.right);
            var m = new Mesh { name = "AtlasSlab" };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        static void Quad(List<Vector3> v, List<int> t, List<Vector3> n, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            for (int k = 0; k < 4; k++) n.Add(normal);
            // Unity draws clockwise triangles; order each quad so its front faces the normal.
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f;
            if (!flip) { t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
            else { t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2); }
        }

        /// Unit sphere (radius 1).
        public static Mesh Sphere(int rings, int segments)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float th = Mathf.PI * r / rings;
                for (int s = 0; s <= segments; s++)
                {
                    float ph = 2f * Mathf.PI * s / segments;
                    v.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)));
                }
            }
            int w = segments + 1;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = r * w + s, b = a + w;
                    // Clockwise seen from outside.
                    t.Add(a); t.Add(a + 1); t.Add(b);
                    t.Add(a + 1); t.Add(b + 1); t.Add(b);
                }
            var m = new Mesh { name = "AtlasSphere" };
            m.SetVertices(v); m.SetNormals(v); m.SetTriangles(t, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return m;
        }

        /// Cylinder of radius 0.5 and height 1 standing on the XZ plane (the table).
        public static Mesh Cylinder(int segments = 64)
        {
            var v = new Vector3[segments * 4 + 2];
            var t = new int[segments * 12];
            int top = segments * 4, bottom = top + 1;
            v[top] = new Vector3(0, 1, 0); v[bottom] = Vector3.zero;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float c = Mathf.Cos(a) * 0.5f, s = Mathf.Sin(a) * 0.5f;
                v[i] = new Vector3(c, 1, s);                 // top rim
                v[segments + i] = new Vector3(c, 1, s);      // side top
                v[segments * 2 + i] = new Vector3(c, 0, s);  // side bottom
                v[segments * 3 + i] = new Vector3(c, 0, s);  // bottom rim
            }
            int k = 0;
            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;
                t[k++] = top; t[k++] = j; t[k++] = i;
                t[k++] = segments + i; t[k++] = segments + j; t[k++] = segments * 2 + i;
                t[k++] = segments * 2 + i; t[k++] = segments + j; t[k++] = segments * 2 + j;
                t[k++] = bottom; t[k++] = segments * 3 + i; t[k++] = segments * 3 + j;
            }
            var m = new Mesh { name = "AtlasTable", vertices = v, triangles = t };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }

    public static class Hex
    {
        public static Color Color(string hex, float alpha = 1f)
        {
            Color c;
            if (string.IsNullOrEmpty(hex) || !ColorUtility.TryParseHtmlString(hex, out c)) c = UnityEngine.Color.white;
            c.a = alpha;
            return c;
        }
    }
}
