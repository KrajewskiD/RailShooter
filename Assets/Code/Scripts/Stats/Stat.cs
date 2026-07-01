using System.Collections.Generic;

public readonly struct StatModifier
{
    public readonly float Value;
    public readonly StatModType Type;

    public StatModifier(float value, StatModType type)
    {
        Value = value;
        Type = type;
    }
}

public class Stat
{
    private float _baseValue;
    public float BaseValue
    {
        get => _baseValue;
        set
        {
            if (UnityEngine.Mathf.Approximately(_baseValue, value)) return; 
            
            _baseValue = value;
            _isDirty = true;
            OnValueChanged?.Invoke();
        }
    }

    private readonly List<StatModifier> _modifiers = new List<StatModifier>();
    
    private bool _isDirty = true;
    private float _cachedValue;
    
    public event System.Action OnValueChanged;

    public Stat(float baseValue)
    {
        _baseValue = baseValue;
        _isDirty = true;
    }

    public float Value 
    {
        get
        {
            if (_isDirty)
            {
                _cachedValue = CalculateFinalValue();
                _isDirty = false;
            }
            return _cachedValue;
        }
    }

    public void AddModifier(StatModifier mod)
    {
        _modifiers.Add(mod);
        _isDirty = true;
        OnValueChanged?.Invoke();
    }

    public float PreviewValueWith(float overriddenBaseValue)
    {
        float finalValue = overriddenBaseValue;
        float sumPercentAdd = 0;

        for (int i = 0; i < _modifiers.Count; i++)
        {
            if (_modifiers[i].Type == StatModType.Flat)
                finalValue += _modifiers[i].Value;
            else if (_modifiers[i].Type == StatModType.PercentAdd)
                sumPercentAdd += _modifiers[i].Value;
        }

        return finalValue * (1 + sumPercentAdd);
    }

    private float CalculateFinalValue()
    {
        float finalValue = _baseValue;
        float sumPercentAdd = 0;


        for (int i = 0; i < _modifiers.Count; i++)
        {
            if (_modifiers[i].Type == StatModType.Flat)
                finalValue += _modifiers[i].Value;
            else if (_modifiers[i].Type == StatModType.PercentAdd)
                sumPercentAdd += _modifiers[i].Value;
        }

        return finalValue * (1 + sumPercentAdd);
    }
}