using System.ComponentModel;
using AllModsSky.Core;
using AllModsSky.App.Mvvm;

namespace AllModsSky.App.ViewModels;

/// <summary>列表里的一行模组。</summary>
public sealed class ModItemViewModel : ObservableObject
{
    private readonly Action<ModItemViewModel>? _onEnabledChanged;
    private bool _enabled;
    private bool _isSelected;
    private int _index;
    private string _formatText = "";
    private string _warning = "";
    private bool _hasWarning;
    private string _compatText = "";
    private bool _healthReady;

    public ModItemViewModel(ModRecord record, Action<ModItemViewModel>? onEnabledChanged = null)
    {
        Record = record;
        _enabled = record.Enabled;
        _onEnabledChanged = onEnabledChanged;
    }

    public ModRecord Record { get; }

    public string Id => Record.Id;
    public string Name => Record.Name;
    public string Source => Record.Source;
    public string VersionText => Record.VersionText;
    public string SizeText => Record.SizeText;
    public bool IsLegacyPak => Record.IsLegacyPak;
    public string KindText => Record.IsLegacyPak ? "pak" : "";
    public string MetaText => $"{VersionText} · {SizeText} · {Source}";

    public int Index
    {
        get => _index;
        set
        {
            if (SetProperty(ref _index, value)) Raise(nameof(IndexText));
        }
    }

    public string IndexText => _index.ToString("00");

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            Record.Enabled = value;
            Raise();
            _onEnabledChanged?.Invoke(this);
        }
    }

    public void SetEnabledSilently(bool value)
    {
        if (_enabled == value) return;
        _enabled = value;
        Record.Enabled = value;
        Raise(nameof(Enabled));
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string FormatText
    {
        get => _formatText;
        private set => SetProperty(ref _formatText, value);
    }

    public string WarningText
    {
        get => _warning;
        private set
        {
            if (SetProperty(ref _warning, value)) Raise(nameof(HasWarning));
        }
    }

    public bool HasWarning => _hasWarning;

    public string CompatText
    {
        get => _compatText;
        private set => SetProperty(ref _compatText, value);
    }

    public bool IsHealthReady
    {
        get => _healthReady;
        private set => SetProperty(ref _healthReady, value);
    }

    public void ApplyHealth(ModHealth health)
    {
        FormatText = health.Summary;
        WarningText = health.Warning;
        CompatText = health.CompatText;
        _hasWarning = health.HasWarning;
        IsHealthReady = true;
        Raise(nameof(HasWarning));
    }

    public string Tooltip
    {
        get
        {
            var lines = new List<string> { Name };
            lines.Add("目录：" + Record.EffectiveFolder);
            lines.Add("格式：" + (string.IsNullOrEmpty(FormatText) ? "正在检查…" : FormatText));
            if (!string.IsNullOrEmpty(CompatText)) lines.Add("适配：" + CompatText);
            if (!string.IsNullOrEmpty(WarningText)) lines.Add("⚠ " + WarningText);
            return string.Join(Environment.NewLine, lines);
        }
    }
}

public sealed class ExternalModItemViewModel
{
    public ExternalModItemViewModel(ExternalMod model)
    {
        Model = model;
    }

    public ExternalMod Model { get; }
    public string Name => Model.Name;
    public string StateText => Model.StateText;
    public string ActionText => Model.ActionText;
    public string KindText => Model.KindText;
    public bool Enabled => Model.Enabled;
}
