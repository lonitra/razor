﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using Microsoft.AspNetCore.Razor.Telemetry;
using Microsoft.CodeAnalysis.Razor.Editor;
using Microsoft.VisualStudio.Editor.Razor;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;

namespace Microsoft.VisualStudio.LanguageServerClient.Razor.Options;

[Shared]
[Export(typeof(OptionsStorage))]
[Export(typeof(IAdvancedSettingsStorage))]
internal class OptionsStorage : IAdvancedSettingsStorage
{
    private readonly WritableSettingsStore _writableSettingsStore;
    private readonly ILanguageServiceBroker2 _languageServiceBroker;
    private readonly ITelemetryReporter _telemetryReporter;
    private const string Collection = "Razor";

    [ImportingConstructor]
    public OptionsStorage(SVsServiceProvider vsServiceProvider, ILanguageServiceBroker2 languageServiceBroker, ITelemetryReporter telemetryReporter)
    {
        var shellSettingsManager = new ShellSettingsManager(vsServiceProvider);
        _writableSettingsStore = shellSettingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

        _writableSettingsStore.CreateCollection(Collection);
        _languageServiceBroker = languageServiceBroker;
        _telemetryReporter = telemetryReporter;
    }

    public event EventHandler<ClientAdvancedSettingsChangedEventArgs>? Changed;
    public ClientAdvancedSettings GetAdvancedSettings() => new(FormatOnType);

    public bool GetBool(string name, bool defaultValue)
    {
        if (_writableSettingsStore.PropertyExists(Collection, name))
        {
            return _writableSettingsStore.GetBoolean(Collection, name);
        }

        return defaultValue;
    }

    public void SetBool(string name, bool value)
    {
        _writableSettingsStore.SetBoolean(Collection, name, value);
        _telemetryReporter.ReportEvent("OptionChanged", Telemetry.TelemetrySeverity.Normal, new Dictionary<string, bool>()
        {
            { name, value }
        }.ToImmutableDictionary());

        NotifyChange();
    }

    private void NotifyChange()
    {
        Changed?.Invoke(this, new ClientAdvancedSettingsChangedEventArgs(GetAdvancedSettings()));
    }

    private const string FormatOnTypeName = "FormatOnType";

    public bool FormatOnType
    {
        get => GetBool(FormatOnTypeName, defaultValue: true);
        set => SetBool(FormatOnTypeName, value);
    }
}
