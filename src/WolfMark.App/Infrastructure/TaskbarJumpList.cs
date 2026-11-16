using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace WolfMark.App.Infrastructure;

/// <summary>
/// The taskbar jump list (M8-03, ADR 0013): New note, Today, Search – shell links to this exe with a
/// <c>wolfmark://</c> link, which a running instance takes over like any later launch. Built with
/// ICustomDestinationList (the WinRT JumpList needs package identity, which the unpackaged app does not have).
/// COM objects through CsWin32's unmanaged interfaces (no built-in COM marshalling under Native AOT; their methods
/// throw on a failed HRESULT), on a thread of its own in the MTA. Not in demo or portable mode – nothing of this copy stays in the system.
/// </summary>
internal static unsafe class TaskbarJumpList
{
    private static readonly Guid DestinationListClsid = new("77f10cf0-3db5-4966-b520-b7c54fd35ed6");
    private static readonly Guid EnumerableObjectCollectionClsid = new("2d3468c1-36a7-43b6-ac24-d3f02fd9607a");
    private static readonly Guid ShellLinkClsid = new("00021401-0000-0000-c000-000000000046");

    /// <summary>Replaces the tasks of the jump list. Blocks until done; call off the UI thread.</summary>
    /// <param name="arguments">Before each task's link: <c>--demo …</c> or nothing.</param>
    public static void Update(IReadOnlyList<(string Title, string Link)> tasks, string arguments) =>
        RunInMta(() => Build(tasks, arguments));

    /// <summary>Removes the jump list of this app (portable mode switched on).</summary>
    public static void Clear() => RunInMta(static () =>
    {
        var list = Create<ICustomDestinationList>(DestinationListClsid);
        try
        {
            list->DeleteList(default);
        }
        finally
        {
            list->Release();
        }
    });

    private static void Build(IReadOnlyList<(string Title, string Link)> tasks, string arguments)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
        var list = Create<ICustomDestinationList>(DestinationListClsid);
        var collection = Create<IObjectCollection>(EnumerableObjectCollectionClsid);
        try
        {
            uint slots;
            void* removed;
            var arrayIid = IObjectArray.IID_Guid;
            list->BeginList(&slots, &arrayIid, &removed);
            ((IUnknown*)removed)->Release();

            foreach (var (title, link) in tasks)
            {
                var shellLink = CreateLink(exe, (arguments + " \"" + link + "\"").Trim(), title);
                try
                {
                    collection->AddObject((IUnknown*)shellLink);
                }
                finally
                {
                    shellLink->Release();
                }
            }

            IObjectArray* array;
            var iid = IObjectArray.IID_Guid;
            ((IUnknown*)collection)->QueryInterface(&iid, (void**)&array).ThrowOnFailure();
            try
            {
                list->AddUserTasks(array);
            }
            finally
            {
                array->Release();
            }

            list->CommitList();
        }
        finally
        {
            collection->Release();
            list->Release();
        }
    }

    private static IShellLinkW* CreateLink(string exe, string arguments, string title)
    {
        var link = Create<IShellLinkW>(ShellLinkClsid);
        try
        {
            fixed (char* path = exe, args = arguments, description = title)
            {
                link->SetPath(path);
                link->SetArguments(args);
                link->SetIconLocation(path, 0);
                link->SetDescription(description);
            }

            // The text shown in the jump list is the link's title property, not its description.
            IPropertyStore* store;
            var iid = IPropertyStore.IID_Guid;
            ((IUnknown*)link)->QueryInterface(&iid, (void**)&store).ThrowOnFailure();
            try
            {
                var value = default(PROPVARIANT);
                value.Anonymous.Anonymous.vt = VARENUM.VT_LPWSTR;
                value.Anonymous.Anonymous.Anonymous.pwszVal = (char*)Marshal.StringToCoTaskMemUni(title);
                try
                {
                    var key = PInvoke.PKEY_Title;
                    store->SetValue(&key, &value);
                    store->Commit();
                }
                finally
                {
                    PInvoke.PropVariantClear(&value); // frees the string (CoTaskMemFree)
                }
            }
            finally
            {
                store->Release();
            }

            return link;
        }
        catch
        {
            link->Release();
            throw;
        }
    }

    private static T* Create<T>(Guid clsid)
        where T : unmanaged, IComIID
    {
        void* instance;
        var iid = T.Guid;
        PInvoke.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, &iid, &instance).ThrowOnFailure();
        return (T*)instance;
    }

    /// <summary>COM work on a new MTA thread (the thread pool's apartment is not guaranteed under Native AOT).</summary>
    private static void RunInMta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var init = PInvoke.CoInitializeEx(null, COINIT.COINIT_MULTITHREADED);
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                if (init.Succeeded)
                {
                    PInvoke.CoUninitialize();
                }
            }
        })
        {
            IsBackground = true,
            Name = "WolfMark jump list",
        };
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new IOException("The jump list could not be updated: " + failure.Message, failure);
        }
    }
}
