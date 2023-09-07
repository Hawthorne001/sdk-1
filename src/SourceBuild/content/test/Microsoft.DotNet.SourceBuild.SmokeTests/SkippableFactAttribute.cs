﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using Xunit;

namespace Microsoft.DotNet.SourceBuild.SmokeTests;

/// <summary>
/// A Fact that will be skipped based on the specified environment variable's value.
/// </summary>
internal class SkippableFactAttribute : FactAttribute
{
    public SkippableFactAttribute(string envName, bool skipOnNullOrWhiteSpaceEnv = false, bool skipOnTrueEnv = false, bool skipOnFalseEnv = false, string[] skipArchitectures = null) =>
        EvaluateSkips(skipOnNullOrWhiteSpaceEnv, skipOnTrueEnv, skipOnFalseEnv, skipArchitectures, (skip) => Skip = skip, envName);

    public SkippableFactAttribute(string[] envNames, bool skipOnNullOrWhiteSpaceEnv = false, bool skipOnTrueEnv = false, bool skipOnFalseEnv = false, string[] skipArchitectures = null) =>
        EvaluateSkips(skipOnNullOrWhiteSpaceEnv, skipOnTrueEnv, skipOnFalseEnv, skipArchitectures, (skip) => Skip = skip, envNames);

    public static void EvaluateSkips(bool skipOnNullOrWhiteSpaceEnv, bool skipOnTrueEnv, bool skipOnFalseEnv, string[] skipArchitectures, Action<string> setSkip, params string[] envNames)
    {
        foreach (string envName in envNames)
        {
            string? envValue = Environment.GetEnvironmentVariable(envName);

            if (skipOnNullOrWhiteSpaceEnv && string.IsNullOrWhiteSpace(envValue))
            {
                setSkip($"Skipping because `{envName}` is null or whitespace");
                break;
            }
            else if ((skipOnTrueEnv || skipOnFalseEnv) && bool.TryParse(envValue, out bool boolValue))
            {
                if (skipOnTrueEnv && boolValue)
                {
                    setSkip($"Skipping because `{envName}` is set to True");
                    break;
                }
                else if (skipOnFalseEnv && !boolValue)
                {
                    setSkip($"Skipping because `{envName}` is set to False");
                    break;
                }
            }
        }

        if (skipArchitectures != null) {
            string? arch = Config.TargetArchitecture;
            if (skipArchitectures.Contains(arch))
            {
                setSkip($"Skipping because arch is `{arch}`");
            }
        }
    }
}
