using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Motor de fizica pe CPU (Prototip 1).
/// Pipeline per pas: integrare (gravitatie) -> grid spatial (broad phase) ->
/// coliziuni pereche (narrow phase + impuls + frecare Coulomb) -> pereti cutie -> sleeping.
/// </summary>
public class PhysicsWorld
{
    public readonly List<PhysicsBody> Bodies = new List<PhysicsBody>();

    // Cutia (toti cei 6 pereti sunt logici; vizual lipseste fata dinspre camera)
    public Vector3 BoxMin, BoxMax;

    public Vector3 Gravity = new Vector3(0f, -9.81f, 0f);
    public float Restitution = 0.3f;     // elasticitatea ciocnirilor
    public float Friction = 0.4f;        // coeficient frecare Coulomb intre obiecte
    public float WallFriction = 0.5f;    // frecare cu peretii
    public float Damping = 0.9995f;      // amortizare usoara a vitezei
    public int Iterations = 2;           // iteratii de rezolvare pe pas
    public float SleepSpeed = 0.12f;     // sub aceasta viteza obiectul poate "adormi"
    public int SleepFrames = 45;         // dupa cate cadre linistite adoarme

    const float WakeSpeed = 1.0f;        // viteza de impact care trezeste un obiect adormit
    const float RestitutionThreshold = 0.5f; // sub aceasta viteza de impact: fara "bounce"
    const float Slop = 0.005f;           // toleranta de penetrare
    const float CorrectionPercent = 0.8f;
    const float MaxCorrection = 0.2f;    // limita corectie pozitie / iteratie

    // ---------------- Grid spatial uniform (counting sort) ----------------
    int[] cellOf = new int[0];
    int[] cellItems = new int[0];
    int[] cellStart = new int[1];
    int[] cellFill = new int[1];
    int nx, ny, nz;
    Vector3 invCell;

    public int SleepingCount
    {
        get { int c = 0; for (int i = 0; i < Bodies.Count; i++) if (Bodies[i].Sleeping) c++; return c; }
    }

    // =====================================================================
    public void Step(float dt)
    {
        // 1. Integrare Euler semi-implicit: v += g*dt ; p += v*dt
        for (int i = 0; i < Bodies.Count; i++)
        {
            PhysicsBody b = Bodies[i];
            if (b.Sleeping) continue;
            b.Vel += Gravity * dt;
            b.Vel *= Damping;
            b.Pos += b.Vel * dt;
        }

        // 2. Broad phase
        BuildGrid();

        // 3. Coliziuni + pereti (de mai multe ori pentru stabilitate)
        for (int it = 0; it < Iterations; it++)
        {
            ResolvePairs();
            for (int i = 0; i < Bodies.Count; i++)
                if (!Bodies[i].Sleeping) Walls(Bodies[i]);
        }

        // 4. Sleeping
        UpdateSleep();
    }

    /// <summary>Da o miscare aleatoare tuturor obiectelor (trezeste scena).</summary>
    public void Kick(float speed)
    {
        for (int i = 0; i < Bodies.Count; i++)
        {
            PhysicsBody b = Bodies[i];
            b.Sleeping = false;
            b.SleepTimer = 0;
            b.Vel += Random.onUnitSphere * speed + Vector3.up * speed * 0.5f;
        }
    }

    // =====================================================================
    //  BROAD PHASE
    // =====================================================================
    void BuildGrid()
    {
        int n = Bodies.Count;
        if (n == 0) return;

        float maxHalf = 0.01f;
        for (int i = 0; i < n; i++)
        {
            Vector3 h = Bodies[i].Half;
            maxHalf = Mathf.Max(maxHalf, Mathf.Max(h.x, Mathf.Max(h.y, h.z)));
        }
        float cellSize = 2f * maxHalf; // celula >= diametrul celui mai mare obiect

        Vector3 size = BoxMax - BoxMin;
        nx = Mathf.Max(1, Mathf.FloorToInt(size.x / cellSize));
        ny = Mathf.Max(1, Mathf.FloorToInt(size.y / cellSize));
        nz = Mathf.Max(1, Mathf.FloorToInt(size.z / cellSize));
        invCell = new Vector3(nx / size.x, ny / size.y, nz / size.z);

        int cells = nx * ny * nz;
        if (cellStart.Length != cells + 1)
        {
            cellStart = new int[cells + 1];
            cellFill = new int[cells + 1];
        }
        if (cellOf.Length < n)
        {
            cellOf = new int[n * 2];
            cellItems = new int[n * 2];
        }

        Array.Clear(cellStart, 0, cells + 1);
        for (int i = 0; i < n; i++)
        {
            int c = CellIndex(Bodies[i].Pos);
            cellOf[i] = c;
            cellStart[c + 1]++;
        }
        for (int c = 0; c < cells; c++) cellStart[c + 1] += cellStart[c];
        Array.Copy(cellStart, cellFill, cells + 1);
        for (int i = 0; i < n; i++) cellItems[cellFill[cellOf[i]]++] = i;
    }

    int CellIndex(Vector3 p)
    {
        int x = Mathf.Clamp((int)((p.x - BoxMin.x) * invCell.x), 0, nx - 1);
        int y = Mathf.Clamp((int)((p.y - BoxMin.y) * invCell.y), 0, ny - 1);
        int z = Mathf.Clamp((int)((p.z - BoxMin.z) * invCell.z), 0, nz - 1);
        return (z * ny + y) * nx + x;
    }

    void ResolvePairs()
    {
        int n = Bodies.Count;
        for (int i = 0; i < n; i++)
        {
            PhysicsBody a = Bodies[i];
            int c = cellOf[i];
            int cx = c % nx;
            int cy = (c / nx) % ny;
            int cz = c / (nx * ny);

            for (int dz = -1; dz <= 1; dz++)
            {
                int z = cz + dz; if (z < 0 || z >= nz) continue;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int y = cy + dy; if (y < 0 || y >= ny) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx; if (x < 0 || x >= nx) continue;
                        int nc = (z * ny + y) * nx + x;
                        for (int k = cellStart[nc]; k < cellStart[nc + 1]; k++)
                        {
                            int j = cellItems[k];
                            if (j <= i) continue; // fiecare pereche o singura data
                            PhysicsBody b = Bodies[j];
                            if (a.Sleeping && b.Sleeping) continue;

                            Vector3 nrm; float pen;
                            if (Contact(a, b, out nrm, out pen))
                                Resolve(a, b, nrm, pen);
                        }
                    }
                }
            }
        }
    }

    // =====================================================================
    //  NARROW PHASE  (normala n pointeaza de la a catre b)
    // =====================================================================
    static bool Contact(PhysicsBody a, PhysicsBody b, out Vector3 n, out float pen)
    {
        n = Vector3.up; pen = 0f;
        if (a.IsSphere && b.IsSphere) return SphereSphere(a, b, out n, out pen);
        if (a.IsSphere) return SphereBox(a, b, out n, out pen);
        if (b.IsSphere)
        {
            bool hit = SphereBox(b, a, out n, out pen);
            n = -n;
            return hit;
        }
        return BoxBox(a, b, out n, out pen);
    }

    // |c1-c2| < r1+r2
    static bool SphereSphere(PhysicsBody a, PhysicsBody b, out Vector3 n, out float pen)
    {
        Vector3 d = b.Pos - a.Pos;
        float r = a.Radius + b.Radius;
        float d2 = d.sqrMagnitude;
        n = Vector3.up; pen = 0f;
        if (d2 >= r * r) return false;
        float dist = Mathf.Sqrt(d2);
        n = dist > 1e-6f ? d / dist : Vector3.up;
        pen = r - dist;
        return true;
    }

    // Sfera vs AABB: cel mai apropiat punct al cutiei de centrul sferei (clamp). n: sfera -> cutie.
    static bool SphereBox(PhysicsBody s, PhysicsBody box, out Vector3 n, out float pen)
    {
        Vector3 local = s.Pos - box.Pos;
        Vector3 h = box.Half;
        Vector3 closest = new Vector3(
            Mathf.Clamp(local.x, -h.x, h.x),
            Mathf.Clamp(local.y, -h.y, h.y),
            Mathf.Clamp(local.z, -h.z, h.z));
        Vector3 diff = local - closest;
        float d2 = diff.sqrMagnitude;
        n = Vector3.up; pen = 0f;
        if (d2 > s.Radius * s.Radius) return false;

        if (d2 > 1e-8f)
        {
            float dist = Mathf.Sqrt(d2);
            n = -diff / dist;               // de la sfera spre cutie
            pen = s.Radius - dist;
        }
        else
        {   // centrul sferei e in interiorul cutiei: iesire pe axa cu penetrare minima
            float dx = h.x - Mathf.Abs(local.x);
            float dy = h.y - Mathf.Abs(local.y);
            float dz = h.z - Mathf.Abs(local.z);
            Vector3 outDir;
            float m;
            if (dx <= dy && dx <= dz) { m = dx; outDir = new Vector3(Mathf.Sign(local.x), 0, 0); }
            else if (dy <= dz)        { m = dy; outDir = new Vector3(0, Mathf.Sign(local.y), 0); }
            else                      { m = dz; outDir = new Vector3(0, 0, Mathf.Sign(local.z)); }
            n = -outDir;
            pen = s.Radius + m;
        }
        return true;
    }

    // AABB vs AABB: suprapunere pe toate 3 axele, normala = axa cu penetrarea minima.
    static bool BoxBox(PhysicsBody a, PhysicsBody b, out Vector3 n, out float pen)
    {
        Vector3 d = b.Pos - a.Pos;
        float ox = a.Half.x + b.Half.x - Mathf.Abs(d.x);
        float oy = a.Half.y + b.Half.y - Mathf.Abs(d.y);
        float oz = a.Half.z + b.Half.z - Mathf.Abs(d.z);
        n = Vector3.up; pen = 0f;
        if (ox <= 0f || oy <= 0f || oz <= 0f) return false;

        if (ox <= oy && ox <= oz) { pen = ox; n = new Vector3(d.x >= 0 ? 1 : -1, 0, 0); }
        else if (oy <= oz)        { pen = oy; n = new Vector3(0, d.y >= 0 ? 1 : -1, 0); }
        else                      { pen = oz; n = new Vector3(0, 0, d.z >= 0 ? 1 : -1); }
        return true;
    }

    // =====================================================================
    //  COLLISION RESPONSE: impuls + frecare Coulomb
    // =====================================================================
    void Resolve(PhysicsBody a, PhysicsBody b, Vector3 n, float pen)
    {
        // trezire daca impactul e puternic
        Vector3 rv = b.Vel - a.Vel;
        float vn = Vector3.Dot(rv, n);
        if (-vn > WakeSpeed) { Wake(a); Wake(b); }

        // un corp adormit se comporta ca obiect cu masa infinita
        float ima = a.Sleeping ? 0f : a.InvMass;
        float imb = b.Sleeping ? 0f : b.InvMass;
        float sum = ima + imb;
        if (sum <= 0f) return;

        // corectie de pozitie (elimina penetrarea)
        float corr = Mathf.Min(Mathf.Max(pen - Slop, 0f) / sum * CorrectionPercent, MaxCorrection);
        a.Pos -= n * (corr * ima);
        b.Pos += n * (corr * imb);

        if (vn > 0f) return; // se departeaza deja

        // impuls normal: j = -(1+e) * vn / (1/m1 + 1/m2)
        float e = (-vn < RestitutionThreshold) ? 0f : Restitution;
        float j = -(1f + e) * vn / sum;
        Vector3 impulse = j * n;
        a.Vel -= impulse * ima;
        b.Vel += impulse * imb;

        // frecare Coulomb: |jt| <= mu * j
        rv = b.Vel - a.Vel;
        Vector3 vt = rv - Vector3.Dot(rv, n) * n;
        float vtm = vt.magnitude;
        if (vtm > 1e-5f)
        {
            Vector3 t = vt / vtm;
            float jt = -vtm / sum;
            float maxF = Friction * j;
            jt = Mathf.Clamp(jt, -maxF, maxF);
            a.Vel -= t * (jt * ima);
            b.Vel += t * (jt * imb);
        }
    }

    // =====================================================================
    //  PERETII CUTIEI (AABB static, masa infinita)
    // =====================================================================
    void Walls(PhysicsBody b)
    {
        for (int ax = 0; ax < 3; ax++)
        {
            float lo = BoxMin[ax] + b.Half[ax];
            float hi = BoxMax[ax] - b.Half[ax];
            if (b.Pos[ax] < lo)      { b.Pos[ax] = lo; WallHit(b, ax, 1f); }
            else if (b.Pos[ax] > hi) { b.Pos[ax] = hi; WallHit(b, ax, -1f); }
        }
    }

    // s = +1/-1: sensul normalei peretelui (spre interiorul cutiei)
    void WallHit(PhysicsBody b, int ax, float s)
    {
        float vn = b.Vel[ax] * s;
        if (vn >= 0f) return;

        float e = (-vn < RestitutionThreshold) ? 0f : Restitution;
        float dvn = -(1f + e) * vn;       // variatia vitezei normale
        b.Vel[ax] = s * (-e * vn);

        // frecare cu peretele: reduce viteza tangentiala cu cel mult mu * dvn
        Vector3 vt = b.Vel; vt[ax] = 0f;
        float m = vt.magnitude;
        if (m > 1e-5f)
        {
            float dv = Mathf.Min(m, WallFriction * dvn);
            b.Vel -= vt / m * dv;
        }
    }

    // =====================================================================
    //  SLEEPING
    // =====================================================================
    static void Wake(PhysicsBody b) { b.Sleeping = false; b.SleepTimer = 0; }

    void UpdateSleep()
    {
        float thr = SleepSpeed * SleepSpeed;
        for (int i = 0; i < Bodies.Count; i++)
        {
            PhysicsBody b = Bodies[i];
            if (b.Sleeping) continue;
            if (b.Vel.sqrMagnitude < thr)
            {
                if (++b.SleepTimer > SleepFrames) { b.Sleeping = true; b.Vel = Vector3.zero; }
            }
            else b.SleepTimer = 0;
        }
    }
}