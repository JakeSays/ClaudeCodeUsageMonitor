using System;
using System.Runtime.InteropServices;
using System.Text;


namespace ClaudeUsageMonitor.Services;

/// <summary>
/// macOS Keychain access via Security.framework P/Invoke.
/// </summary>
internal static partial class MacKeychain
{
    private const string SecurityLib = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";

    private const uint kCFStringEncodingUTF8 = 0x08000100;
    private const int errSecSuccess = 0;
    private const int errSecItemNotFound = -25300;
    private const int errSecDuplicateItem = -25299;

    // Lazy-loaded Security/CF constant symbols
    private static readonly nint SecLib;
    private static readonly nint CfLib;

    private static readonly nint KSecClass;
    private static readonly nint KSecClassGenericPassword;
    private static readonly nint KSecAttrService;
    private static readonly nint KSecAttrAccount;
    private static readonly nint KSecReturnData;
    private static readonly nint KSecValueData;
    private static readonly nint KSecMatchLimit;
    private static readonly nint KSecMatchLimitOne;
    private static readonly nint KCFBooleanTrue;
    private static readonly nint KCFTypeDictionaryKeyCallBacks;
    private static readonly nint KCFTypeDictionaryValueCallBacks;

    static MacKeychain()
    {
        SecLib = dlopen(SecurityLib, 0);
        CfLib = dlopen(CoreFoundationLib, 0);

        KSecClass = ReadSymbol(SecLib, "kSecClass");
        KSecClassGenericPassword = ReadSymbol(SecLib, "kSecClassGenericPassword");
        KSecAttrService = ReadSymbol(SecLib, "kSecAttrService");
        KSecAttrAccount = ReadSymbol(SecLib, "kSecAttrAccount");
        KSecReturnData = ReadSymbol(SecLib, "kSecReturnData");
        KSecValueData = ReadSymbol(SecLib, "kSecValueData");
        KSecMatchLimit = ReadSymbol(SecLib, "kSecMatchLimit");
        KSecMatchLimitOne = ReadSymbol(SecLib, "kSecMatchLimitOne");
        KCFBooleanTrue = ReadSymbol(CfLib, "kCFBooleanTrue");

        // These are pointers to structs — pass directly (don't dereference)
        KCFTypeDictionaryKeyCallBacks = dlsym(CfLib, "kCFTypeDictionaryKeyCallBacks");
        KCFTypeDictionaryValueCallBacks = dlsym(CfLib, "kCFTypeDictionaryValueCallBacks");
    }

    /// <summary>
    /// Reads a generic password from the Keychain by service name.
    /// Returns the UTF-8 password string, or null if not found.
    /// </summary>
    public static string? Read(string serviceName)
    {
        nint service = 0;
        nint query = 0;

        try
        {
            service = CreateCFString(serviceName);

            nint[] keys =
            [
                KSecClass,
                KSecAttrService,
                KSecReturnData,
                KSecMatchLimit
            ];
            nint[] values =
            [
                KSecClassGenericPassword,
                service,
                KCFBooleanTrue,
                KSecMatchLimitOne
            ];

            query = CFDictionaryCreate(0, keys, values, keys.Length,
                KCFTypeDictionaryKeyCallBacks, KCFTypeDictionaryValueCallBacks);

            var status = SecItemCopyMatching(query, out var result);

            if (status == errSecItemNotFound || result == 0)
            {
                return null;
            }

            if (status != errSecSuccess)
            {
                throw new InvalidOperationException($"SecItemCopyMatching failed with status {status}");
            }

            try
            {
                return ExtractCFDataString(result);
            }
            finally
            {
                CFRelease(result);
            }
        }
        finally
        {
            if (query != 0)
            {
                CFRelease(query);
            }
            if (service != 0)
            {
                CFRelease(service);
            }
        }
    }

    /// <summary>
    /// Writes a generic password to the Keychain. If the item already exists,
    /// updates it atomically via SecItemUpdate (no delete step).
    /// </summary>
    public static void Write(string serviceName, string account, string data)
    {
        nint service = 0;
        nint acct = 0;
        nint passwordData = 0;
        nint addDict = 0;

        try
        {
            service = CreateCFString(serviceName);
            acct = CreateCFString(account);
            var dataBytes = Encoding.UTF8.GetBytes(data);
            passwordData = CFDataCreate(0, dataBytes, dataBytes.Length);

            nint[] addKeys =
            [
                KSecClass,
                KSecAttrService,
                KSecAttrAccount,
                KSecValueData
            ];
            nint[] addValues =
            [
                KSecClassGenericPassword,
                service,
                acct,
                passwordData
            ];

            addDict = CFDictionaryCreate(0, addKeys, addValues, addKeys.Length,
                KCFTypeDictionaryKeyCallBacks, KCFTypeDictionaryValueCallBacks);

            var status = SecItemAdd(addDict, 0);

            if (status == errSecDuplicateItem)
            {
                // Item exists — update in place
                nint queryDict = 0;
                nint updateDict = 0;

                try
                {
                    nint[] queryKeys = [KSecClass, KSecAttrService, KSecAttrAccount];
                    nint[] queryValues = [KSecClassGenericPassword, service, acct];

                    queryDict = CFDictionaryCreate(0, queryKeys, queryValues, queryKeys.Length,
                        KCFTypeDictionaryKeyCallBacks, KCFTypeDictionaryValueCallBacks);

                    nint[] updateKeys = [KSecValueData];
                    nint[] updateValues = [passwordData];

                    updateDict = CFDictionaryCreate(0, updateKeys, updateValues, updateKeys.Length,
                        KCFTypeDictionaryKeyCallBacks, KCFTypeDictionaryValueCallBacks);

                    status = SecItemUpdate(queryDict, updateDict);

                    if (status != errSecSuccess)
                    {
                        throw new InvalidOperationException($"SecItemUpdate failed with status {status}");
                    }
                }
                finally
                {
                    if (queryDict != 0)
                    {
                        CFRelease(queryDict);
                    }
                    if (updateDict != 0)
                    {
                        CFRelease(updateDict);
                    }
                }
            }
            else if (status != errSecSuccess)
            {
                throw new InvalidOperationException($"SecItemAdd failed with status {status}");
            }
        }
        finally
        {
            if (addDict != 0)
            {
                CFRelease(addDict);
            }
            if (passwordData != 0)
            {
                CFRelease(passwordData);
            }
            if (acct != 0)
            {
                CFRelease(acct);
            }
            if (service != 0)
            {
                CFRelease(service);
            }
        }
    }

    private static nint ReadSymbol(nint lib, string name)
    {
        var ptr = dlsym(lib, name);
        if (ptr == 0)
        {
            throw new EntryPointNotFoundException($"Symbol not found: {name}");
        }
        return Marshal.ReadIntPtr(ptr);
    }

    private static nint CreateCFString(string value) =>
        CFStringCreateWithCString(0, value, kCFStringEncodingUTF8);

    private static string ExtractCFDataString(nint cfData)
    {
        var length = (int) CFDataGetLength(cfData);
        var ptr = CFDataGetBytePtr(cfData);
        var buffer = new byte[length];
        Marshal.Copy(ptr, buffer, 0, length);
        return Encoding.UTF8.GetString(buffer);
    }

    // --- P/Invoke declarations ---

    [LibraryImport(LibSystem, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint dlopen(string path, int mode);

    [LibraryImport(LibSystem, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint dlsym(nint handle, string symbol);

    [LibraryImport(CoreFoundationLib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CFStringCreateWithCString(nint alloc, string cStr, uint encoding);

    [LibraryImport(CoreFoundationLib)]
    private static partial void CFRelease(nint cf);

    [LibraryImport(CoreFoundationLib)]
    private static partial long CFDataGetLength(nint theData);

    [LibraryImport(CoreFoundationLib)]
    private static partial nint CFDataGetBytePtr(nint theData);

    [LibraryImport(CoreFoundationLib)]
    private static partial nint CFDictionaryCreate(
        nint allocator,
        nint[] keys,
        nint[] values,
        long numValues,
        nint keyCallBacks,
        nint valueCallBacks);

    [LibraryImport(CoreFoundationLib)]
    private static partial nint CFDataCreate(nint allocator, byte[] bytes, long length);

    [LibraryImport(SecurityLib)]
    private static partial int SecItemCopyMatching(nint query, out nint result);

    [LibraryImport(SecurityLib)]
    private static partial int SecItemAdd(nint attributes, nint result);

    [LibraryImport(SecurityLib)]
    private static partial int SecItemUpdate(nint query, nint attributesToUpdate);
}
