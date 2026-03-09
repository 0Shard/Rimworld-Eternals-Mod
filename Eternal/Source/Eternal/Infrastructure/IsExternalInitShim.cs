/*
 * Relative Path: Eternal/Source/Eternal/Infrastructure/IsExternalInitShim.cs
 * Creation Date: 19-02-2026
 * Last Edit: 19-02-2026
 * Author: 0Shard
 * Description: Compiler polyfill enabling record struct and init-only property syntax on .NET Framework 4.7.2.
 *              The C# compiler requires this class to exist in the System.Runtime.CompilerServices namespace
 *              to emit the IsExternalInit attribute for init accessors and record types.
 *              Without this shim, record struct and init properties cannot be used on net472.
 */

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
