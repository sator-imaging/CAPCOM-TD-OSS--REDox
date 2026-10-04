// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace REDox.Yaml;

internal static class ThrowHelper
{
    [DoesNotReturn]
    public static void ThrowInvalidOperation(string message) =>
        throw new InvalidOperationException(message);

    [DoesNotReturn]
    public static void ThrowArgumentOutOfRange(string paramName, string message) =>
        throw new ArgumentOutOfRangeException(paramName, message);

    [DoesNotReturn]
    public static void ThrowDecodeFallback(string message) =>
        throw new DecoderFallbackException(message);

    [DoesNotReturn]
    public static void ThrowNotSupported(string message) =>
        throw new NotSupportedException(message);
}
