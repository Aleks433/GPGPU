using UnityEngine;

public enum BodyType { Sphere, Cube, Cylinder }

/// <summary>
/// Un corp fizic. Sfera foloseste Bounding Sphere (Half = (r,r,r)).
/// Cubul si cilindrul folosesc AABB (Half = semi-dimensiunile pe fiecare axa).
/// Campurile sunt publice (nu proprietati) ca sa se poata scrie Pos[axa] = ...
/// </summary>
public class PhysicsBody
{
    public BodyType Type;
    public Vector3 Pos;
    public Vector3 Vel;
    public Vector3 Half;      // semi-dimensiuni (AABB) sau (r,r,r) pentru sfera
    public float Radius;      // doar pentru sfera
    public float InvMass;
    public bool Sleeping;
    public int SleepTimer;
    public Transform Visual;

    public bool IsSphere { get { return Type == BodyType.Sphere; } }
}