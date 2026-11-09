using System.Diagnostics;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Pune acest script pe un GameObject gol din scena.
/// Creeaza cutia, spawneaza obiecte, ruleaza fizica pe CPU si afiseaza FPS + timp fizica.
/// Taste: 1 = sfera, 2 = cub, 3 = cilindru (cate 10), R = impuls aleator, C = sterge tot.
/// </summary>
public class PhysicsDemo : MonoBehaviour
{
    [Header("Cutia")]
    public Vector3 boxSize = new Vector3(14f, 10f, 14f);

    [Header("Parametri fizica")]
    public float restitution = 0.3f;
    public float friction = 0.4f;
    public float wallFriction = 0.5f;
    public float kickSpeed = 5f;

    [Header("Materiale (optional)")]
    public Material sphereMat, cubeMat, cylinderMat, wallMat;

    PhysicsWorld world = new PhysicsWorld();
    const float FixedDt = 1f / 60f; // un pas fix pe cadru (ca sa se vada costul fizicii)

    Stopwatch sw = new Stopwatch();
    float physicsMs;       // medie mobila
    float fps;             // medie mobila

    void Start()
    {
        world.BoxMin = new Vector3(-boxSize.x / 2f, 0f, -boxSize.z / 2f);
        world.BoxMax = new Vector3(boxSize.x / 2f, boxSize.y, boxSize.z / 2f);
        BuildBoxVisuals();

        Camera cam = Camera.main;
        if (cam != null)
        {
            // camera priveste dinspre -Z (fata fara mesh e cea de la z = min)
            cam.transform.position = new Vector3(0f, boxSize.y * 0.55f, -boxSize.z * 1.3f);
            cam.transform.LookAt(new Vector3(0f, boxSize.y * 0.4f, 0f));
        }
    }

    void Update()
    {
        // setari live
        world.Restitution = restitution;
        world.Friction = friction;
        world.WallFriction = wallFriction;

        HandleKeys();

        // --- fizica, cronometrata ---
        sw.Restart();
        world.Step(FixedDt);
        sw.Stop();
        float ms = (float)sw.Elapsed.TotalMilliseconds;
        physicsMs = Mathf.Lerp(physicsMs, ms, 0.05f);

        // --- sincronizare vizuala ---
        var bodies = world.Bodies;
        for (int i = 0; i < bodies.Count; i++)
        {
            PhysicsBody b = bodies[i];
            if (!b.Sleeping) b.Visual.position = b.Pos;
        }

        fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-5f), 0.05f);
    }

    // ------------------------------------------------------------------
    void HandleKeys()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) SpawnMany(BodyType.Sphere, 10);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SpawnMany(BodyType.Cube, 10);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SpawnMany(BodyType.Cylinder, 10);
        if (Input.GetKeyDown(KeyCode.R)) world.Kick(kickSpeed);
        if (Input.GetKeyDown(KeyCode.C)) ClearAll();
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 330, 520), GUI.skin.box);
        GUILayout.Label("FPS: " + fps.ToString("F1") + "   |   Fizica CPU: " + physicsMs.ToString("F2") + " ms");
        GUILayout.Label("Obiecte: " + world.Bodies.Count + "  (adormite: " + world.SleepingCount + ")");

        GUILayout.Space(6);
        GUILayout.Label("Mod 1 - generare manuala");
        if (GUILayout.Button("+10 Sfere   [1]")) SpawnMany(BodyType.Sphere, 10);
        if (GUILayout.Button("+10 Cuburi  [2]")) SpawnMany(BodyType.Cube, 10);
        if (GUILayout.Button("+10 Cilindri [3]")) SpawnMany(BodyType.Cylinder, 10);
        if (GUILayout.Button("Impuls aleator (trezeste scena) [R]")) world.Kick(kickSpeed);
        if (GUILayout.Button("Sterge tot [C]")) ClearAll();

        GUILayout.Space(6);
        GUILayout.Label("Mod 2 - predefinit (sfere / cuburi / cilindri)");
        if (GUILayout.Button("100 / 250 / 500"))   Preset(100, 250, 500);
        if (GUILayout.Button("250 / 500 / 1000"))  Preset(250, 500, 1000);
        if (GUILayout.Button("500 / 1000 / 2500")) Preset(500, 1000, 2500);
        GUILayout.EndArea();
    }

    // ------------------------------------------------------------------
    void Preset(int spheres, int cubes, int cylinders)
    {
        ClearAll();
        SpawnMany(BodyType.Sphere, spheres);
        SpawnMany(BodyType.Cube, cubes);
        SpawnMany(BodyType.Cylinder, cylinders);
    }

    void ClearAll()
    {
        foreach (var b in world.Bodies) if (b.Visual != null) Destroy(b.Visual.gameObject);
        world.Bodies.Clear();
    }

    void SpawnMany(BodyType t, int count)
    {
        for (int i = 0; i < count; i++) Spawn(t);
    }

    void Spawn(BodyType t)
    {
        PhysicsBody b = new PhysicsBody { Type = t };
        PrimitiveType prim;
        Vector3 scale;
        float volume;
        Material mat;

        float r = Random.Range(0.25f, 0.5f);
        switch (t)
        {
            case BodyType.Sphere:
                b.Radius = r;
                b.Half = new Vector3(r, r, r);
                prim = PrimitiveType.Sphere;
                scale = Vector3.one * (2f * r);
                volume = 4f / 3f * Mathf.PI * r * r * r;
                mat = sphereMat;
                break;
            case BodyType.Cube:
                b.Half = new Vector3(r, r, r);
                prim = PrimitiveType.Cube;
                scale = Vector3.one * (2f * r);
                volume = 8f * r * r * r;
                mat = cubeMat;
                break;
            default: // Cylinder: aproximat cu AABB (bounding volume)
                float hh = Random.Range(0.3f, 0.7f);
                b.Half = new Vector3(r, hh, r);
                prim = PrimitiveType.Cylinder;           // Unity: inaltime 2, raza 0.5
                scale = new Vector3(2f * r, hh, 2f * r);
                volume = Mathf.PI * r * r * 2f * hh;
                mat = cylinderMat;
                break;
        }
        b.InvMass = 1f / volume; // densitate = 1

        // pozitie + viteza + directie aleatoare
        b.Pos = new Vector3(
            Random.Range(world.BoxMin.x + b.Half.x, world.BoxMax.x - b.Half.x),
            Random.Range(world.BoxMin.y + b.Half.y, world.BoxMax.y - b.Half.y),
            Random.Range(world.BoxMin.z + b.Half.z, world.BoxMax.z - b.Half.z));
        b.Vel = Random.onUnitSphere * Random.Range(1f, 6f);

        GameObject go = GameObject.CreatePrimitive(prim);
        Destroy(go.GetComponent<Collider>());            // NU folosim fizica Unity
        go.transform.SetParent(transform, false);
        go.transform.localScale = scale;
        go.transform.position = b.Pos;
        Renderer rend = go.GetComponent<Renderer>();
        if (mat != null) rend.sharedMaterial = mat;
        else rend.material.color = Color.HSVToRGB(Random.value, 0.6f, 0.95f);
        b.Visual = go.transform;

        world.Bodies.Add(b);
    }

    // ------------------------------------------------------------------
    // Pereti vizuali: podea, spate, stanga, dreapta. Plafonul si fata din fata
    // (z = min) exista DOAR logic in fizica, nu au mesh.
    void BuildBoxVisuals()
    {
        float t = 0.2f;
        Vector3 s = boxSize;
        MakeWall(new Vector3(0, -t / 2f, 0), new Vector3(s.x + 2 * t, t, s.z + 2 * t));          // podea
        MakeWall(new Vector3(0, s.y / 2f, s.z / 2f + t / 2f), new Vector3(s.x + 2 * t, s.y, t)); // spate
        MakeWall(new Vector3(-s.x / 2f - t / 2f, s.y / 2f, 0), new Vector3(t, s.y, s.z));        // stanga
        MakeWall(new Vector3(s.x / 2f + t / 2f, s.y / 2f, 0), new Vector3(t, s.y, s.z));         // dreapta
    }

    void MakeWall(Vector3 pos, Vector3 scale)
    {
        GameObject w = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(w.GetComponent<Collider>());
        w.transform.SetParent(transform, false);
        w.transform.position = pos;
        w.transform.localScale = scale;
        Renderer r = w.GetComponent<Renderer>();
        if (wallMat != null) r.sharedMaterial = wallMat;
        else r.material.color = new Color(0.75f, 0.75f, 0.8f);
    }
}