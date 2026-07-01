using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

[DefaultExecutionOrder(-150)]
public class MasterDecorationPool : MonoBehaviour
{
    public static MasterDecorationPool Instance { get; private set; }

    [Header("Slot Pool")]
    [SerializeField] private int maxSlots = 500;
    [SerializeField] private int instancesPerSlot = 2048;
    [SerializeField] private int maxDecorationTypes = 64;

    [StructLayout(LayoutKind.Sequential)]
    public struct SlotMeta
    {
        public Vector3 boundsCenter;
        public float pad0;
        public Vector3 boundsExtents;
        public float pad1;
        public static int Size => 32;
    }

    private GraphicsBuffer _masterBuffer;
    private GraphicsBuffer _slotMasterCounters;
    private GraphicsBuffer _slotMetaBuffer;
    private GraphicsBuffer _activeSlotsBuffer;

    private SlotMeta[] _slotMetaCPU;
    private uint[] _activeSlotsCPU;
    private Stack<int> _freeSlots;
    private HashSet<int> _activeSlotsSet;
    private bool _activeSlotsDirty;

    private readonly uint[] _zeroCounter = new uint[1] { 0u };

    public int MaxSlots => maxSlots;
    public int InstancesPerSlot => instancesPerSlot;
    public int MaxDecorationTypes => maxDecorationTypes;
    public int ActiveSlotCount => _activeSlotsSet?.Count ?? 0;

    public GraphicsBuffer MasterBuffer => _masterBuffer;
    public GraphicsBuffer SlotMasterCounters => _slotMasterCounters;
    public GraphicsBuffer SlotMetaBuffer => _slotMetaBuffer;
    public GraphicsBuffer ActiveSlotsBuffer => _activeSlotsBuffer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        Initialize();
    }

    private void Initialize()
    {
        long total = (long)maxSlots * instancesPerSlot;
        _masterBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)total, MasterDecorationSpawnResult.Size);
        _slotMasterCounters = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxSlots, sizeof(uint));
        _slotMetaBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxSlots, SlotMeta.Size);
        _activeSlotsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxSlots, sizeof(uint));

        _slotMetaCPU = new SlotMeta[maxSlots];
        _activeSlotsCPU = new uint[maxSlots];

        _slotMasterCounters.SetData(new uint[maxSlots]);

        _freeSlots = new Stack<int>(maxSlots);
        _activeSlotsSet = new HashSet<int>(maxSlots);
        for (int i = maxSlots - 1; i >= 0; i--) _freeSlots.Push(i);
        _activeSlotsDirty = true;
    }

    public int AcquireSlot(Vector3 boundsCenter, Vector3 boundsExtents)
    {
        if (_freeSlots.Count == 0)
        {
            return -1;
        }

        int slot = _freeSlots.Pop();
        _activeSlotsSet.Add(slot);
        _activeSlotsDirty = true;

        _slotMetaCPU[slot] = new SlotMeta
        {
            boundsCenter = boundsCenter,
            boundsExtents = boundsExtents
        };


        _slotMetaBuffer.SetData(_slotMetaCPU, slot, slot, 1);

        return slot;
    }

    public void ReleaseSlot(int slot)
    {
        if (slot < 0) return;
        if (!_activeSlotsSet.Remove(slot)) return;

        _freeSlots.Push(slot);
        _activeSlotsDirty = true;
    }

    public void UpdateSlotBounds(int slot, Vector3 boundsCenter, Vector3 boundsExtents)
    {
        if (slot < 0 || slot >= maxSlots) return;
        _slotMetaCPU[slot] = new SlotMeta { boundsCenter = boundsCenter, boundsExtents = boundsExtents };
        _slotMetaBuffer.SetData(_slotMetaCPU, slot, slot, 1);
    }

    public void RefreshActiveSlotsIfDirty()
    {
        if (!_activeSlotsDirty) return;

        int count = 0;
        foreach (var s in _activeSlotsSet)
        {
            if (count >= _activeSlotsCPU.Length) break;
            _activeSlotsCPU[count++] = (uint)s;
        }

        if (count > 0) _activeSlotsBuffer.SetData(_activeSlotsCPU, 0, 0, count);
        _activeSlotsDirty = false;
    }

    public void ClearSlotCounterGPU(ComputeShader shader, int clearKernel, int slot)
    {
        if (slot < 0) return;
        shader.SetBuffer(clearKernel, "_SlotMasterCounters", _slotMasterCounters);
        shader.SetInt("_TargetSlotID", slot);
        shader.Dispatch(clearKernel, 1, 1, 1);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        _masterBuffer?.Release(); _masterBuffer = null;
        _slotMasterCounters?.Release(); _slotMasterCounters = null;
        _slotMetaBuffer?.Release(); _slotMetaBuffer = null;
        _activeSlotsBuffer?.Release(); _activeSlotsBuffer = null;
    }
}
