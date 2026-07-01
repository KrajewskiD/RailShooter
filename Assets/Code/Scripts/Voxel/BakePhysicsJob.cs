using Unity.Jobs;
using UnityEngine;
public struct BakePhysicsJob : IJob
{
    public int MeshInstanceID;
    public bool Convex;

    public void Execute()
    {
        Physics.BakeMesh(MeshInstanceID, Convex);
    }
}