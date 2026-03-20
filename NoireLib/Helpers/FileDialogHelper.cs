using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// Provides simple access to native operating system file and directory pickers.
/// </summary>
public static class FileDialogHelper
{
    private const string LoggerPrefix = $"[{nameof(FileDialogHelper)}] ";
    private static readonly SemaphoreSlim DialogLock = new(1, 1);
    private static readonly object SavedPathSync = new();
    private static readonly WindowsDialogDispatcher WindowsDialogs = new();
    private static string savedPath = ".";

    /// <summary>
    /// Opens the native folder picker and returns the selected folder.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="startPath">The initial folder, or <see langword="null"/> to reuse the last successful location.</param>
    /// <param name="allowCreate">Whether the native dialog should allow creating a new folder when the platform supports it.</param>
    /// <param name="cancellationToken">A token used to cancel waiting for the dialog to open.</param>
    /// <returns>A task that resolves to the selected folder path, or <see langword="null"/> if the dialog was cancelled.</returns>
    public static async Task<string?> PickFolderAsync(string title, string? startPath = null, bool allowCreate = true, CancellationToken cancellationToken = default)
    {
        var response = await ShowDialogAsync(new DialogRequest(DialogKind.OpenFolder, title, string.Empty, startPath ?? GetSavedPath(), ".", string.Empty, 1, allowCreate), cancellationToken).ConfigureAwait(false);
        return response.IsOk ? response.Paths.FirstOrDefault() : null;
    }

    /// <summary>
    /// Opens the native folder picker and returns the selected folders.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="selectionCountMax">The maximum number of folders to keep from the selection. Use 0 for no limit.</param>
    /// <param name="startPath">The initial folder, or <see langword="null"/> to reuse the last successful location.</param>
    /// <param name="allowCreate">Whether the native dialog should allow creating a new folder when the platform supports it.</param>
    /// <param name="cancellationToken">A token used to cancel waiting for the dialog to open.</param>
    /// <returns>A task that resolves to the selected folder paths, or an empty collection if the dialog was cancelled.</returns>
    public static async Task<IReadOnlyList<string>> PickFoldersAsync(string title, int selectionCountMax = 0, string? startPath = null, bool allowCreate = true, CancellationToken cancellationToken = default)
    {
        var response = await ShowDialogAsync(new DialogRequest(DialogKind.OpenFolder, title, string.Empty, startPath ?? GetSavedPath(), ".", string.Empty, selectionCountMax <= 0 ? 0 : selectionCountMax, allowCreate), cancellationToken).ConfigureAwait(false);
        return response.IsOk ? response.Paths : Array.Empty<string>();
    }

    /// <summary>
    /// Opens the native file picker and returns the selected file.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="filters">The file filters using a Dalamud-compatible extension format.</param>
    /// <param name="startPath">The initial folder, or <see langword="null"/> to reuse the last successful location.</param>
    /// <param name="cancellationToken">A token used to cancel waiting for the dialog to open.</param>
    /// <returns>A task that resolves to the selected file path, or <see langword="null"/> if the dialog was cancelled.</returns>
    public static async Task<string?> PickFileAsync(string title, string filters = "", string? startPath = null, CancellationToken cancellationToken = default)
    {
        var response = await ShowDialogAsync(new DialogRequest(DialogKind.OpenFile, title, filters, startPath ?? GetSavedPath(), ".", string.Empty, 1, false), cancellationToken).ConfigureAwait(false);
        return response.IsOk ? response.Paths.FirstOrDefault() : null;
    }

    /// <summary>
    /// Opens the native file picker and returns the selected files.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="filters">The file filters using a Dalamud-compatible extension format.</param>
    /// <param name="selectionCountMax">The maximum number of files to keep from the selection. Use 0 for no limit.</param>
    /// <param name="startPath">The initial folder, or <see langword="null"/> to reuse the last successful location.</param>
    /// <param name="cancellationToken">A token used to cancel waiting for the dialog to open.</param>
    /// <returns>A task that resolves to the selected file paths, or an empty collection if the dialog was cancelled.</returns>
    public static async Task<IReadOnlyList<string>> PickFilesAsync(string title, string filters = "", int selectionCountMax = 0, string? startPath = null, CancellationToken cancellationToken = default)
    {
        var response = await ShowDialogAsync(new DialogRequest(DialogKind.OpenFile, title, filters, startPath ?? GetSavedPath(), ".", string.Empty, selectionCountMax <= 0 ? 0 : selectionCountMax, false), cancellationToken).ConfigureAwait(false);
        return response.IsOk ? response.Paths : Array.Empty<string>();
    }

    /// <summary>
    /// Opens the native save file picker and returns the chosen file path.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="filters">The file filters using a Dalamud-compatible extension format.</param>
    /// <param name="defaultFileName">The default file name to suggest.</param>
    /// <param name="defaultExtension">The default extension to append when the platform supports it.</param>
    /// <param name="startPath">The initial folder, or <see langword="null"/> to reuse the last successful location.</param>
    /// <param name="cancellationToken">A token used to cancel waiting for the dialog to open.</param>
    /// <returns>A task that resolves to the chosen file path, or <see langword="null"/> if the dialog was cancelled.</returns>
    public static async Task<string?> SaveFileAsync(string title, string filters, string defaultFileName, string defaultExtension, string? startPath = null, CancellationToken cancellationToken = default)
    {
        var response = await ShowDialogAsync(new DialogRequest(DialogKind.SaveFile, title, filters, startPath ?? GetSavedPath(), defaultFileName, defaultExtension, 1, true), cancellationToken).ConfigureAwait(false);
        return response.IsOk ? response.Paths.FirstOrDefault() : null;
    }

    /// <summary>
    /// Opens the native folder picker and invokes a callback when it closes.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="callback">The callback that receives the dialog result.</param>
    /// <param name="startPath">The initial folder, or <see langword="null"/> to reuse the last successful location.</param>
    /// <param name="allowCreate">Whether the native dialog should allow creating a new folder when the platform supports it.</param>
    /// <param name="cancellationToken">A token used to cancel waiting for the dialog to open.</param>
    /// <returns>A task that completes after the callback has been invoked.</returns>
    public static Task PickFolder(string title, Action<bool, string> callback, string? startPath = null, bool allowCreate = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return ExecuteSingleSelectionAsync(new DialogRequest(DialogKind.OpenFolder, title, string.Empty, startPath ?? GetSavedPath(), ".", string.Empty, 1, allowCreate), callback, cancellationToken);
    }

    /// <summary>
    /// Opens the native folder picker for multiple folders and invokes a callback when it closes.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="callback">The callback that receives the dialog result.</param>
    /// <param name="selectionCountMax">The maximum number of folders to keep from the selection. Use 0 for no limit.</param>
    /// <param name="startPath">The initial folder, or <see langword="null"/> to reuse the last successful location.</param>
    /// <param name="allowCreate">Whether the native dialog should allow creating a new folder when the platform supports it.</param>
    /// <param name="cancellationToken">A token used to cancel waiting for the dialog to open.</param>
    /// <returns>A task that completes after the callback has been invoked.</returns>
    public static Task PickFolders(string title, Action<bool, List<string>> callback, int selectionCountMax = 0, string? startPath = null, bool allowCreate = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return ExecuteMultiSelectionAsync(new DialogRequest(DialogKind.OpenFolder, title, string.Empty, startPath ?? GetSavedPath(), ".", string.Empty, selectionCountMax <= 0 ? 0 : selectionCountMax, allowCreate), callback, cancellationToken);
    }

    /// <summary>
    /// Opens the native file picker and invokes a callback when it closes.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="filters">The file filters using a Dalamud-compatible extension format.</param>
    /// <param name="callback">The callback that receives the dialog result.</param>
    /// <param name="startPath">The initial folder, or <see langword="null"/> to reuse the last successful location.</param>
    /// <param name="cancellationToken">A token used to cancel waiting for the dialog to open.</param>
    /// <returns>A task that completes after the callback has been invoked.</returns>
    public static Task PickFile(string title, string filters, Action<bool, string> callback, string? startPath = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return ExecuteSingleSelectionAsync(new DialogRequest(DialogKind.OpenFile, title, filters, startPath ?? GetSavedPath(), ".", string.Empty, 1, false), callback, cancellationToken);
    }

    /// <summary>
    /// Opens the native file picker for multiple files and invokes a callback when it closes.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="filters">The file filters using a Dalamud-compatible extension format.</param>
    /// <param name="callback">The callback that receives the dialog result.</param>
    /// <param name="selectionCountMax">The maximum number of files to keep from the selection. Use 0 for no limit.</param>
    /// <param name="startPath">The initial folder, or <see langword="null"/> to reuse the last successful location.</param>
    /// <param name="cancellationToken">A token used to cancel waiting for the dialog to open.</param>
    /// <returns>A task that completes after the callback has been invoked.</returns>
    public static Task PickFiles(string title, string filters, Action<bool, List<string>> callback, int selectionCountMax = 0, string? startPath = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return ExecuteMultiSelectionAsync(new DialogRequest(DialogKind.OpenFile, title, filters, startPath ?? GetSavedPath(), ".", string.Empty, selectionCountMax <= 0 ? 0 : selectionCountMax, false), callback, cancellationToken);
    }

    /// <summary>
    /// Opens the native save file picker and invokes a callback when it closes.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="filters">The file filters using a Dalamud-compatible extension format.</param>
    /// <param name="defaultFileName">The default file name to suggest.</param>
    /// <param name="defaultExtension">The default extension to append when the platform supports it.</param>
    /// <param name="callback">The callback that receives the dialog result.</param>
    /// <param name="startPath">The initial folder, or <see langword="null"/> to reuse the last successful location.</param>
    /// <param name="cancellationToken">A token used to cancel waiting for the dialog to open.</param>
    /// <returns>A task that completes after the callback has been invoked.</returns>
    public static Task SaveFile(string title, string filters, string defaultFileName, string defaultExtension, Action<bool, string> callback, string? startPath = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return ExecuteSingleSelectionAsync(new DialogRequest(DialogKind.SaveFile, title, filters, startPath ?? GetSavedPath(), defaultFileName, defaultExtension, 1, true), callback, cancellationToken);
    }

    /// <summary>
    /// Opens the native folder picker with a callback signature matching Dalamud's manager.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="callback">The callback that receives the dialog result.</param>
    /// <returns>A task that completes after the callback has been invoked.</returns>
    public static Task OpenFolderDialog(string title, Action<bool, string> callback)
    {
        return PickFolder(title, callback, GetSavedPath(), allowCreate: false);
    }

    /// <summary>
    /// Opens the native folder picker with a callback signature matching Dalamud's manager.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="callback">The callback that receives the dialog result.</param>
    /// <param name="startPath">The initial folder, or <see langword="null"/> to reuse the last successful location.</param>
    /// <param name="isModal">Retained for API compatibility. Native dialogs are already modal at the operating system level.</param>
    /// <returns>A task that completes after the callback has been invoked.</returns>
    public static Task OpenFolderDialog(string title, Action<bool, string> callback, string? startPath, bool isModal = false)
    {
        _ = isModal;
        return PickFolder(title, callback, startPath, allowCreate: false);
    }

    /// <summary>
    /// Opens the native file picker with a callback signature matching Dalamud's manager.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="filters">The file filters using a Dalamud-compatible extension format.</param>
    /// <param name="callback">The callback that receives the dialog result.</param>
    /// <returns>A task that completes after the callback has been invoked.</returns>
    public static Task OpenFileDialog(string title, string filters, Action<bool, string> callback)
    {
        return PickFile(title, filters, callback, GetSavedPath());
    }

    /// <summary>
    /// Opens the native multi-file picker with a callback signature matching Dalamud's manager.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="filters">The file filters using a Dalamud-compatible extension format.</param>
    /// <param name="callback">The callback that receives the dialog result.</param>
    /// <param name="selectionCountMax">The maximum number of files to keep from the selection. Use 0 for no limit.</param>
    /// <param name="startPath">The initial folder, or <see langword="null"/> to reuse the last successful location.</param>
    /// <param name="isModal">Retained for API compatibility. Native dialogs are already modal at the operating system level.</param>
    /// <returns>A task that completes after the callback has been invoked.</returns>
    public static Task OpenFileDialog(string title, string filters, Action<bool, List<string>> callback, int selectionCountMax, string? startPath = null, bool isModal = false)
    {
        _ = isModal;
        return PickFiles(title, filters, callback, selectionCountMax, startPath);
    }

    /// <summary>
    /// Opens the native save file picker with a callback signature matching Dalamud's manager.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="filters">The file filters using a Dalamud-compatible extension format.</param>
    /// <param name="defaultFileName">The default file name to suggest.</param>
    /// <param name="defaultExtension">The default extension to append when the platform supports it.</param>
    /// <param name="callback">The callback that receives the dialog result.</param>
    /// <returns>A task that completes after the callback has been invoked.</returns>
    public static Task SaveFileDialog(string title, string filters, string defaultFileName, string defaultExtension, Action<bool, string> callback)
    {
        return SaveFile(title, filters, defaultFileName, defaultExtension, callback, GetSavedPath());
    }

    /// <summary>
    /// Opens the native save file picker with a callback signature matching Dalamud's manager.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="filters">The file filters using a Dalamud-compatible extension format.</param>
    /// <param name="defaultFileName">The default file name to suggest.</param>
    /// <param name="defaultExtension">The default extension to append when the platform supports it.</param>
    /// <param name="callback">The callback that receives the dialog result.</param>
    /// <param name="startPath">The initial folder, or <see langword="null"/> to reuse the last successful location.</param>
    /// <param name="isModal">Retained for API compatibility. Native dialogs are already modal at the operating system level.</param>
    /// <returns>A task that completes after the callback has been invoked.</returns>
    public static Task SaveFileDialog(string title, string filters, string defaultFileName, string defaultExtension, Action<bool, string> callback, string? startPath, bool isModal = false)
    {
        _ = isModal;
        return SaveFile(title, filters, defaultFileName, defaultExtension, callback, startPath);
    }

    /// <summary>
    /// Retained for compatibility with code that expects an ImGui-driven dialog manager.
    /// </summary>
    /// <returns>This method does nothing because native dialogs do not require a draw loop.</returns>
    public static void Draw()
    {
    }

    /// <summary>
    /// Retained for compatibility with code that expects an ImGui-driven dialog manager.
    /// </summary>
    /// <returns>This method does nothing because native dialogs are not tracked after closing.</returns>
    public static void Reset()
    {
    }

    private static async Task ExecuteSingleSelectionAsync(DialogRequest request, Action<bool, string> callback, CancellationToken cancellationToken)
    {
        var response = await ShowDialogAsync(request, cancellationToken).ConfigureAwait(false);
        callback(response.IsOk, response.IsOk ? response.Paths.FirstOrDefault() ?? string.Empty : string.Empty);
    }

    private static async Task ExecuteMultiSelectionAsync(DialogRequest request, Action<bool, List<string>> callback, CancellationToken cancellationToken)
    {
        var response = await ShowDialogAsync(request, cancellationToken).ConfigureAwait(false);
        callback(response.IsOk, response.IsOk ? [.. response.Paths] : []);
    }

    private static async Task<DialogResponse> ShowDialogAsync(DialogRequest request, CancellationToken cancellationToken)
    {
        await DialogLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var response = SystemHelper.IsWindows
                ? await WindowsDialogs.InvokeAsync(() => ShowWindowsDialog(request), cancellationToken).ConfigureAwait(false)
                : await Task.Run(() => ShowDialogCore(request), cancellationToken).ConfigureAwait(false);

            if (response.IsOk)
                UpdateSavedPath(response.Paths, request.Kind);

            return response;
        }
        catch (OperationCanceledException)
        {
            return DialogResponse.Cancelled;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Native dialog failed for '{request.Title}'.", LoggerPrefix);
            return DialogResponse.Cancelled;
        }
        finally
        {
            DialogLock.Release();
        }
    }

    private static DialogResponse ShowDialogCore(DialogRequest request)
    {
        if (SystemHelper.IsLinux)
            return ShowLinuxDialog(request);

        if (SystemHelper.IsMacOS)
            return ShowMacDialog(request);

        NoireLogger.LogWarning($"Native dialogs are not supported on '{SystemHelper.OSDescription}'.", LoggerPrefix);
        return DialogResponse.Cancelled;
    }

    private static DialogResponse ShowWindowsDialog(DialogRequest request)
    {
        return request.Kind switch
        {
            DialogKind.OpenFolder => ShowWindowsFolderDialogCore(request),
            DialogKind.OpenFile => ShowWindowsOpenFileDialogCore(request),
            DialogKind.SaveFile => ShowWindowsSaveFileDialogCore(request),
            _ => DialogResponse.Cancelled
        };
    }

    private static DialogResponse ShowWindowsOpenFileDialogCore(DialogRequest request)
    {
        FileOpenDialogComObject? dialogComObject = null;
        IFileOpenDialog? dialog = null;
        IShellItem? resultItem = null;
        nint filterBuffer = IntPtr.Zero;
        var filterCount = 0;

        try
        {
            dialogComObject = new FileOpenDialogComObject();
            dialog = (IFileOpenDialog)dialogComObject;

            dialog.GetOptions(out var options);
            options |= FileOpenDialogOptions.ForceFileSystem | FileOpenDialogOptions.PathMustExist | FileOpenDialogOptions.FileMustExist | FileOpenDialogOptions.NoChangeDirectory;
            if (request.SelectionCountMax != 1)
                options |= FileOpenDialogOptions.AllowMultiSelect;
            dialog.SetOptions(options);

            if (!string.IsNullOrWhiteSpace(request.Title))
                dialog.SetTitle(request.Title);

            var filters = BuildWindowsFilterSpecs(request.Filters);
            if (filters.Length > 0)
            {
                filterBuffer = CreateWindowsFilterSpecBuffer(filters, out filterCount);
                dialog.SetFileTypes((uint)filterCount, filterBuffer);
            }

            var initialDirectory = ResolveDirectoryPath(request.StartPath);
            if (!string.IsNullOrWhiteSpace(initialDirectory))
                dialog.SetFileName(Path.Combine(initialDirectory, "select"));

            var showResult = dialog.Show(IntPtr.Zero);
            if (showResult == HResultCancelled)
                return DialogResponse.Cancelled;

            Marshal.ThrowExceptionForHR(showResult);

            if (request.SelectionCountMax != 1)
            {
                dialog.GetResults(out var shellItemArrayPointer);
                return CreateWindowsMultiFolderResponse(shellItemArrayPointer, request.SelectionCountMax);
            }

            dialog.GetResult(out resultItem);
            resultItem.GetDisplayName(ShellItemDisplayName.FileSystemPath, out var selectedPath);

            return string.IsNullOrWhiteSpace(selectedPath)
                ? DialogResponse.Cancelled
                : new DialogResponse(true, [selectedPath]);
        }
        finally
        {
            FreeWindowsFilterSpecBuffer(filterBuffer, filterCount);
            ReleaseComObject(resultItem);
            ReleaseComObject(dialog);
            ReleaseComObject(dialogComObject);
        }
    }

    private static DialogResponse ShowWindowsFolderDialogCore(DialogRequest request)
    {
        FileOpenDialogComObject? dialogComObject = null;
        IFileOpenDialog? dialog = null;
        IShellItem? resultItem = null;

        try
        {
            dialogComObject = new FileOpenDialogComObject();
            dialog = (IFileOpenDialog)dialogComObject;

            dialog.GetOptions(out var options);
            options |= FileOpenDialogOptions.PickFolders | FileOpenDialogOptions.ForceFileSystem | FileOpenDialogOptions.PathMustExist | FileOpenDialogOptions.NoChangeDirectory;
            if (request.SelectionCountMax != 1)
                options |= FileOpenDialogOptions.AllowMultiSelect;
            dialog.SetOptions(options);

            if (!string.IsNullOrWhiteSpace(request.Title))
                dialog.SetTitle(request.Title);

            dialog.SetOkButtonLabel(request.SelectionCountMax != 1 ? "Select Folders" : "Select Folder");

            var showResult = dialog.Show(IntPtr.Zero);
            if (showResult == HResultCancelled)
                return DialogResponse.Cancelled;

            Marshal.ThrowExceptionForHR(showResult);

            if (request.SelectionCountMax != 1)
            {
                dialog.GetResults(out var shellItemArrayPointer);
                return CreateWindowsMultiFolderResponse(shellItemArrayPointer, request.SelectionCountMax);
            }

            dialog.GetResult(out resultItem);
            resultItem.GetDisplayName(ShellItemDisplayName.FileSystemPath, out var selectedPath);

            return string.IsNullOrWhiteSpace(selectedPath)
                ? DialogResponse.Cancelled
                : new DialogResponse(true, [selectedPath]);
        }
        finally
        {
            ReleaseComObject(resultItem);
            ReleaseComObject(dialog);
            ReleaseComObject(dialogComObject);
        }
    }

    private static DialogResponse ShowWindowsSaveFileDialogCore(DialogRequest request)
    {
        FileSaveDialogComObject? dialogComObject = null;
        IFileSaveDialog? dialog = null;
        IShellItem? resultItem = null;
        nint filterBuffer = IntPtr.Zero;
        var filterCount = 0;

        try
        {
            dialogComObject = new FileSaveDialogComObject();
            dialog = (IFileSaveDialog)dialogComObject;

            dialog.GetOptions(out var options);
            options |= FileOpenDialogOptions.ForceFileSystem | FileOpenDialogOptions.PathMustExist | FileOpenDialogOptions.NoChangeDirectory | FileOpenDialogOptions.OverwritePrompt;
            dialog.SetOptions(options);

            if (!string.IsNullOrWhiteSpace(request.Title))
                dialog.SetTitle(request.Title);

            var filters = BuildWindowsFilterSpecs(request.Filters);
            if (filters.Length > 0)
            {
                filterBuffer = CreateWindowsFilterSpecBuffer(filters, out filterCount);
                dialog.SetFileTypes((uint)filterCount, filterBuffer);
            }

            var initialDirectory = ResolveDirectoryPath(request.StartPath);
            if (!string.IsNullOrWhiteSpace(initialDirectory))
                dialog.SetFileName(Path.Combine(initialDirectory, BuildDefaultFileName(request.DefaultName, request.DefaultExtension)));
            else if (!string.IsNullOrWhiteSpace(request.DefaultName) && request.DefaultName != ".")
                dialog.SetFileName(BuildDefaultFileName(request.DefaultName, request.DefaultExtension));

            var defaultExtension = request.DefaultExtension.TrimStart('.');
            if (!string.IsNullOrWhiteSpace(defaultExtension))
                dialog.SetDefaultExtension(defaultExtension);

            var showResult = dialog.Show(IntPtr.Zero);
            if (showResult == HResultCancelled)
                return DialogResponse.Cancelled;

            Marshal.ThrowExceptionForHR(showResult);

            dialog.GetResult(out resultItem);
            resultItem.GetDisplayName(ShellItemDisplayName.FileSystemPath, out var selectedPath);

            return string.IsNullOrWhiteSpace(selectedPath)
                ? DialogResponse.Cancelled
                : new DialogResponse(true, [selectedPath]);
        }
        finally
        {
            FreeWindowsFilterSpecBuffer(filterBuffer, filterCount);
            ReleaseComObject(resultItem);
            ReleaseComObject(dialog);
            ReleaseComObject(dialogComObject);
        }
    }

    private static DialogResponse ShowLinuxDialog(DialogRequest request)
    {
        foreach (var candidate in new Func<DialogRequest, DialogResponse>[] { ShowLinuxZenityDialog, ShowLinuxKDialog })
        {
            try
            {
                return candidate(request);
            }
            catch (Win32Exception)
            {
            }
            catch (FileNotFoundException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        NoireLogger.LogWarning("No supported Linux native dialog executable was found. Tried 'zenity' and 'kdialog'.", LoggerPrefix);
        return DialogResponse.Cancelled;
    }

    private static DialogResponse ShowLinuxZenityDialog(DialogRequest request)
    {
        var arguments = new List<string> { "--file-selection", $"--title={request.Title}" };
        var initialPath = GetInitialSelectionPath(request);

        if (!string.IsNullOrWhiteSpace(initialPath))
            arguments.Add($"--filename={initialPath}");

        switch (request.Kind)
        {
            case DialogKind.OpenFolder:
                arguments.Add("--directory");
                break;
            case DialogKind.SaveFile:
                arguments.Add("--save");
                arguments.Add("--confirm-overwrite");
                break;
        }

        if (request.SelectionCountMax != 1 && request.Kind is DialogKind.OpenFile or DialogKind.OpenFolder)
        {
            arguments.Add("--multiple");
            arguments.Add("--separator=|");
        }

        foreach (var filter in ParseFilterGroups(request.Filters))
        {
            arguments.Add($"--file-filter={filter.Label} | {string.Join(' ', filter.Patterns)}");
        }

        var result = RunProcess("zenity", arguments);
        return CreateDialogResponse(result, request.SelectionCountMax);
    }

    private static DialogResponse ShowLinuxKDialog(DialogRequest request)
    {
        var arguments = new List<string>();
        var initialPath = GetInitialSelectionPath(request);
        var filter = BuildKDialogFilter(request.Filters);

        switch (request.Kind)
        {
            case DialogKind.OpenFile:
                arguments.Add(request.SelectionCountMax != 1 ? "--getopenfilename" : "--getopenfilename");
                arguments.Add(initialPath);
                arguments.Add(filter);
                arguments.Add("--title");
                arguments.Add(request.Title);
                if (request.SelectionCountMax != 1)
                    arguments.Add("--multiple");
                if (request.SelectionCountMax != 1)
                    arguments.Add("--separate-output");
                break;
            case DialogKind.SaveFile:
                arguments.Add("--getsavefilename");
                arguments.Add(initialPath);
                arguments.Add(filter);
                arguments.Add("--title");
                arguments.Add(request.Title);
                break;
            case DialogKind.OpenFolder:
                if (request.SelectionCountMax != 1)
                    throw new NotSupportedException("kdialog does not support multi-folder picking in this helper.");
                arguments.Add("--getexistingdirectory");
                arguments.Add(ResolveDirectoryPath(request.StartPath));
                arguments.Add("--title");
                arguments.Add(request.Title);
                break;
        }

        var result = RunProcess("kdialog", arguments);
        return CreateDialogResponse(result, request.SelectionCountMax);
    }

    private static DialogResponse ShowMacDialog(DialogRequest request)
    {
        var result = RunProcess("osascript", BuildMacArguments(request));
        return CreateDialogResponse(result, request.SelectionCountMax);
    }

    private static ProcessExecutionResult RunProcess(string fileName, IEnumerable<string> arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();

        process.WaitForExit();
        return new ProcessExecutionResult(process.ExitCode, standardOutput, standardError);
    }

    private static DialogResponse CreateWindowsMultiFolderResponse(IntPtr shellItemArrayPointer, int selectionCountMax)
    {
        IShellItemArray? shellItemArray = null;
        var paths = new List<string>();

        try
        {
            shellItemArray = (IShellItemArray)Marshal.GetObjectForIUnknown(shellItemArrayPointer);
            shellItemArray.GetCount(out var count);

            for (uint index = 0; index < count; index++)
            {
                shellItemArray.GetItemAt(index, out var shellItem);

                try
                {
                    shellItem.GetDisplayName(ShellItemDisplayName.FileSystemPath, out var selectedPath);
                    if (!string.IsNullOrWhiteSpace(selectedPath))
                        paths.Add(selectedPath);
                }
                finally
                {
                    ReleaseComObject(shellItem);
                }
            }

            if (selectionCountMax > 0 && paths.Count > selectionCountMax)
                paths = [.. paths.Take(selectionCountMax)];

            return paths.Count == 0 ? DialogResponse.Cancelled : new DialogResponse(true, paths);
        }
        finally
        {
            if (shellItemArrayPointer != IntPtr.Zero)
                Marshal.Release(shellItemArrayPointer);

            ReleaseComObject(shellItemArray);
        }
    }

    private static DialogResponse CreateDialogResponse(ProcessExecutionResult result, int selectionCountMax)
    {
        if (result.ExitCode != 0)
        {
            if (string.IsNullOrWhiteSpace(result.StandardOutput) && string.IsNullOrWhiteSpace(result.StandardError))
                return DialogResponse.Cancelled;

            NoireLogger.LogWarning($"Native dialog exited with code {result.ExitCode}: {result.StandardError}".Trim(), LoggerPrefix);
            return DialogResponse.Cancelled;
        }

        var paths = ParsePaths(result.StandardOutput);
        if (paths.Count == 0)
            return DialogResponse.Cancelled;

        if (selectionCountMax > 0 && paths.Count > selectionCountMax)
            paths = [.. paths.Take(selectionCountMax)];

        return new DialogResponse(true, paths);
    }

    private static ComDlgFilterSpec[] BuildWindowsFilterSpecs(string filters)
    {
        var groups = ParseFilterGroups(filters);
        if (groups.Count == 0)
            return [new ComDlgFilterSpec("All files", "*.*")];

        return [.. groups.Select(group => new ComDlgFilterSpec(group.Label, string.Join(';', group.Patterns)))];
    }

    private static nint CreateWindowsFilterSpecBuffer(IReadOnlyList<ComDlgFilterSpec> filters, out int filterCount)
    {
        filterCount = filters.Count;
        if (filterCount == 0)
            return IntPtr.Zero;

        var structureSize = Marshal.SizeOf<NativeComDlgFilterSpec>();
        var buffer = Marshal.AllocCoTaskMem(structureSize * filterCount);

        for (var index = 0; index < filterCount; index++)
        {
            var nativeFilter = new NativeComDlgFilterSpec
            {
                Name = Marshal.StringToCoTaskMemUni(filters[index].Name),
                Spec = Marshal.StringToCoTaskMemUni(filters[index].Spec)
            };

            Marshal.StructureToPtr(nativeFilter, buffer + (index * structureSize), false);
        }

        return buffer;
    }

    private static void FreeWindowsFilterSpecBuffer(nint buffer, int filterCount)
    {
        if (buffer == IntPtr.Zero)
            return;

        var structureSize = Marshal.SizeOf<NativeComDlgFilterSpec>();
        for (var index = 0; index < filterCount; index++)
        {
            var nativeFilter = Marshal.PtrToStructure<NativeComDlgFilterSpec>(buffer + (index * structureSize));
            if (nativeFilter.Name != IntPtr.Zero)
                Marshal.FreeCoTaskMem(nativeFilter.Name);
            if (nativeFilter.Spec != IntPtr.Zero)
                Marshal.FreeCoTaskMem(nativeFilter.Spec);
        }

        Marshal.FreeCoTaskMem(buffer);
    }

    private static List<string> ParsePaths(string output)
    {
        return output
            .Split(['\r', '\n', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ReleaseComObject<T>(T? instance) where T : class
    {
        if (instance != null && Marshal.IsComObject(instance))
            Marshal.FinalReleaseComObject(instance);
    }

    private static List<string> BuildMacArguments(DialogRequest request)
    {
        var arguments = new List<string>();
        foreach (var line in BuildMacScriptLines(request))
        {
            arguments.Add("-e");
            arguments.Add(line);
        }

        return arguments;
    }

    private static IEnumerable<string> BuildMacScriptLines(DialogRequest request)
    {
        var title = EscapeAppleScript(request.Title);
        var initialDirectory = EscapeAppleScript(ResolveDirectoryPath(request.StartPath));
        var defaultFileName = EscapeAppleScript(BuildDefaultFileName(request.DefaultName, request.DefaultExtension));
        var extensions = ParseFilterGroups(request.Filters).SelectMany(group => group.Extensions).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        yield return "try";

        switch (request.Kind)
        {
            case DialogKind.OpenFile:
                yield return $"set dialogResult to choose file with prompt \"{title}\" default location POSIX file \"{initialDirectory}\"{BuildMacTypeClause(extensions)} multiple selections allowed {(request.SelectionCountMax != 1 ? "true" : "false")}";
                break;
            case DialogKind.SaveFile:
                yield return $"set dialogResult to choose file name with prompt \"{title}\" default location POSIX file \"{initialDirectory}\" default name \"{defaultFileName}\"";
                break;
            case DialogKind.OpenFolder:
                yield return $"set dialogResult to choose folder with prompt \"{title}\" default location POSIX file \"{initialDirectory}\" multiple selections allowed {(request.SelectionCountMax != 1 ? "true" : "false")}";
                break;
        }

        yield return "if class of dialogResult is list then";
        yield return "set outputLines to {}";
        yield return "repeat with currentItem in dialogResult";
        yield return "set end of outputLines to POSIX path of currentItem";
        yield return "end repeat";
        yield return "set AppleScript's text item delimiters to linefeed";
        yield return "return outputLines as text";
        yield return "else";
        yield return "return POSIX path of dialogResult";
        yield return "end if";
        yield return "on error number -128";
        yield return "return \"\"";
        yield return "end try";
    }

    private static string BuildMacTypeClause(IReadOnlyCollection<string> extensions)
    {
        if (extensions.Count == 0)
            return string.Empty;

        return $" of type {{{string.Join(", ", extensions.Select(extension => $"\"{EscapeAppleScript(extension)}\""))}}}";
    }

    private static string BuildKDialogFilter(string filters)
    {
        var groups = ParseFilterGroups(filters);
        if (groups.Count == 0)
            return "*.*|All files";

        return string.Join('\n', groups.Select(group => $"{string.Join(' ', group.Patterns)}|{group.Label}"));
    }

    private static string BuildWindowsFilter(string filters)
    {
        var groups = ParseFilterGroups(filters);
        if (groups.Count == 0)
            return "All files (*.*)|*.*";

        var builder = new List<string>(groups.Count * 2 + 2);
        foreach (var group in groups)
        {
            builder.Add($"{group.Label} ({string.Join(", ", group.Patterns)})");
            builder.Add(string.Join(';', group.Patterns));
        }

        if (!groups.Any(group => group.Patterns.Contains("*.*", StringComparer.OrdinalIgnoreCase)))
        {
            builder.Add("All files (*.*)");
            builder.Add("*.*");
        }

        return string.Join('|', builder);
    }

    private static List<DialogFilterGroup> ParseFilterGroups(string filters)
    {
        if (string.IsNullOrWhiteSpace(filters))
            return [];

        var groups = new List<DialogFilterGroup>();

        foreach (var token in SplitTopLevel(filters))
        {
            var group = ParseFilterGroup(token);
            if (group.Patterns.Count > 0)
                groups.Add(group);
        }

        if (groups.Count == 0)
        {
            var fallbackPatterns = ParsePatterns(filters);
            if (fallbackPatterns.Count > 0)
                groups.Add(CreateFilterGroup(string.Empty, fallbackPatterns));
        }

        return groups;
    }

    private static DialogFilterGroup ParseFilterGroup(string token)
    {
        token = token.Trim();
        if (string.IsNullOrWhiteSpace(token))
            return DialogFilterGroup.Empty;

        var openBraceIndex = token.IndexOf('{');
        var closeBraceIndex = token.LastIndexOf('}');
        if (openBraceIndex >= 0 && closeBraceIndex > openBraceIndex)
        {
            var label = token[..openBraceIndex].Trim();
            var content = token[(openBraceIndex + 1)..closeBraceIndex];
            return CreateFilterGroup(label, ParsePatterns(content));
        }

        return CreateFilterGroup(string.Empty, ParsePatterns(token));
    }

    private static DialogFilterGroup CreateFilterGroup(string label, IReadOnlyList<string> rawPatterns)
    {
        var patterns = new List<string>(rawPatterns.Count);
        var extensions = new List<string>(rawPatterns.Count);

        foreach (var rawPattern in rawPatterns)
        {
            var normalizedPattern = NormalizePattern(rawPattern);
            if (normalizedPattern == null)
                continue;

            patterns.Add(normalizedPattern);

            var extension = ExtractExtension(normalizedPattern);
            if (extension != null)
                extensions.Add(extension);
        }

        if (patterns.Count == 0)
            return DialogFilterGroup.Empty;

        return new DialogFilterGroup(string.IsNullOrWhiteSpace(label) ? BuildFilterLabel(patterns, extensions) : label, patterns, extensions);
    }

    private static List<string> ParsePatterns(string value)
    {
        return value
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .ToList();
    }

    private static List<string> SplitTopLevel(string value)
    {
        var items = new List<string>();
        var builder = new StringBuilder();
        var braceDepth = 0;

        foreach (var character in value)
        {
            if (character == '{')
                braceDepth++;
            else if (character == '}' && braceDepth > 0)
                braceDepth--;

            if (character == ',' && braceDepth == 0)
            {
                items.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(character);
        }

        if (builder.Length > 0)
            items.Add(builder.ToString());

        return items;
    }

    private static string? NormalizePattern(string pattern)
    {
        pattern = pattern.Trim();
        if (string.IsNullOrWhiteSpace(pattern))
            return null;

        if (pattern is ".*" or "*" or "*.*")
            return "*.*";

        if (pattern.StartsWith("*.", StringComparison.Ordinal))
            return pattern;

        if (pattern.StartsWith(".", StringComparison.Ordinal))
            return $"*{pattern}";

        if (pattern.Contains('*'))
            return pattern;

        return $"*.{pattern.TrimStart('.')}";
    }

    private static string? ExtractExtension(string pattern)
    {
        if (pattern.Equals("*.*", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!pattern.StartsWith("*.", StringComparison.Ordinal))
            return null;

        return pattern[2..].Trim();
    }

    private static string BuildFilterLabel(IReadOnlyList<string> patterns, IReadOnlyList<string> extensions)
    {
        if (patterns.Count == 1 && patterns[0].Equals("*.*", StringComparison.OrdinalIgnoreCase))
            return "All files";

        if (extensions.Count == 1)
            return $"{extensions[0].ToUpperInvariant()} files";

        return "Supported files";
    }

    private static string BuildDefaultFileName(string defaultName, string defaultExtension)
    {
        defaultName = string.IsNullOrWhiteSpace(defaultName) || defaultName == "." ? string.Empty : defaultName.Trim();
        defaultExtension = defaultExtension.Trim();

        if (string.IsNullOrWhiteSpace(defaultExtension))
            return defaultName;

        var normalizedExtension = defaultExtension.StartsWith(".", StringComparison.Ordinal) ? defaultExtension : $".{defaultExtension}";
        if (string.IsNullOrWhiteSpace(defaultName) || defaultName.EndsWith(normalizedExtension, StringComparison.OrdinalIgnoreCase))
            return defaultName;

        return $"{defaultName}{normalizedExtension}";
    }

    private static string GetInitialSelectionPath(DialogRequest request)
    {
        var directory = ResolveDirectoryPath(request.StartPath);

        return request.Kind switch
        {
            DialogKind.SaveFile => Path.Combine(directory, BuildDefaultFileName(request.DefaultName, request.DefaultExtension)),
            _ => directory
        };
    }

    private static string ResolveDirectoryPath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (Directory.Exists(path))
                return path;

            var potentialDirectory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(potentialDirectory) && Directory.Exists(potentialDirectory))
                return potentialDirectory;
        }

        var fallback = GetSavedPath();
        if (Directory.Exists(fallback))
            return fallback;

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private static string EscapeAppleScript(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static void UpdateSavedPath(IReadOnlyList<string> selectedPaths, DialogKind kind)
    {
        if (selectedPaths.Count == 0)
            return;

        var firstPath = selectedPaths[0];
        var nextSavedPath = kind switch
        {
            DialogKind.OpenFolder => firstPath,
            _ => Path.GetDirectoryName(firstPath) ?? firstPath
        };

        if (string.IsNullOrWhiteSpace(nextSavedPath))
            return;

        lock (SavedPathSync)
            savedPath = nextSavedPath;
    }

    private static string GetSavedPath()
    {
        lock (SavedPathSync)
            return savedPath;
    }

    private enum DialogKind
    {
        OpenFile,
        SaveFile,
        OpenFolder
    }

    [Flags]
    private enum FileOpenDialogOptions : uint
    {
        OverwritePrompt = 0x00000002,
        NoChangeDirectory = 0x00000008,
        PickFolders = 0x00000020,
        ForceFileSystem = 0x00000040,
        AllowMultiSelect = 0x00000200,
        FileMustExist = 0x00001000,
        PathMustExist = 0x00000800
    }

    private enum ShellItemDisplayName : uint
    {
        FileSystemPath = 0x80058000
    }

    private sealed record DialogRequest(DialogKind Kind, string Title, string Filters, string StartPath, string DefaultName, string DefaultExtension, int SelectionCountMax, bool AllowCreate);

    private sealed record DialogResponse(bool IsOk, IReadOnlyList<string> Paths)
    {
        public static DialogResponse Cancelled { get; } = new(false, Array.Empty<string>());
    }

    private sealed record DialogFilterGroup(string Label, IReadOnlyList<string> Patterns, IReadOnlyList<string> Extensions)
    {
        public static DialogFilterGroup Empty { get; } = new(string.Empty, Array.Empty<string>(), Array.Empty<string>());
    }

    private sealed record ProcessExecutionResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed record ComDlgFilterSpec([MarshalAs(UnmanagedType.LPWStr)] string Name, [MarshalAs(UnmanagedType.LPWStr)] string Spec);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeComDlgFilterSpec
    {
        public nint Name;
        public nint Spec;
    }

    private sealed class WindowsDialogDispatcher
    {
        private readonly BlockingCollection<WindowsDialogWorkItem> queue = [];

        public WindowsDialogDispatcher()
        {
            var thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "NoireLib.WindowsDialogDispatcher"
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            _ = InvokeAsync(PrewarmDialogs);
        }

        public Task<DialogResponse> InvokeAsync(Func<DialogResponse> callback, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<DialogResponse>(cancellationToken);

            var completionSource = new TaskCompletionSource<DialogResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration cancellationRegistration = default;

            if (cancellationToken.CanBeCanceled)
            {
                cancellationRegistration = cancellationToken.Register(static state =>
                {
                    var source = (TaskCompletionSource<DialogResponse>)state!;
                    source.TrySetCanceled();
                }, completionSource);
            }

            queue.Add(new WindowsDialogWorkItem(callback, completionSource, cancellationRegistration), cancellationToken);
            return completionSource.Task;
        }

        private void Run()
        {
            foreach (var workItem in queue.GetConsumingEnumerable())
            {
                if (workItem.CompletionSource.Task.IsCanceled)
                {
                    workItem.CancellationRegistration.Dispose();
                    continue;
                }

                try
                {
                    workItem.CompletionSource.SetResult(workItem.Callback());
                }
                catch (Exception ex)
                {
                    workItem.CompletionSource.SetException(ex);
                }
                finally
                {
                    workItem.CancellationRegistration.Dispose();
                }
            }
        }

        private static DialogResponse PrewarmDialogs()
        {
            FileOpenDialogComObject? openDialogComObject = null;
            IFileOpenDialog? openDialog = null;
            FileSaveDialogComObject? saveDialogComObject = null;
            IFileSaveDialog? saveDialog = null;

            try
            {
                openDialogComObject = new FileOpenDialogComObject();
                openDialog = (IFileOpenDialog)openDialogComObject;
                saveDialogComObject = new FileSaveDialogComObject();
                saveDialog = (IFileSaveDialog)saveDialogComObject;
            }
            catch
            {
            }
            finally
            {
                ReleaseComObject(saveDialog);
                ReleaseComObject(saveDialogComObject);
                ReleaseComObject(openDialog);
                ReleaseComObject(openDialogComObject);
            }

            return DialogResponse.Cancelled;
        }
    }

    private sealed record WindowsDialogWorkItem(Func<DialogResponse> Callback, TaskCompletionSource<DialogResponse> CompletionSource, CancellationTokenRegistration CancellationRegistration);

    private const int HResultCancelled = unchecked((int)0x800704C7);

    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialogComObject;

    [ComImport]
    [Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B")]
    private class FileSaveDialogComObject;

    [ComImport]
    [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);

        void SetFileTypes(uint filterCount, IntPtr filterSpec);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(FileOpenDialogOptions options);
        void GetOptions(out FileOpenDialogOptions options);
        void SetDefaultFolder(IShellItem shellItem);
        void SetFolder(IShellItem shellItem);
        void GetFolder(out IShellItem shellItem);
        void GetCurrentSelection(out IShellItem shellItem);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem shellItem);
        void AddPlace(IShellItem shellItem, uint alignment);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string defaultExtension);
        void Close(int hr);
        void SetClientGuid(in Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr filter);
        void GetResults(out IntPtr shellItemArray);
        void GetSelectedItems(out IntPtr shellItemArray);
    }

    [ComImport]
    [Guid("84bccd23-5fde-4cdb-aea4-af64b83d78ab")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileSaveDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);

        void SetFileTypes(uint filterCount, IntPtr filterSpec);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(FileOpenDialogOptions options);
        void GetOptions(out FileOpenDialogOptions options);
        void SetDefaultFolder(IShellItem shellItem);
        void SetFolder(IShellItem shellItem);
        void GetFolder(out IShellItem shellItem);
        void GetCurrentSelection(out IShellItem shellItem);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem shellItem);
        void AddPlace(IShellItem shellItem, uint alignment);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string defaultExtension);
        void Close(int hr);
        void SetClientGuid(in Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr filter);
        void SetSaveAsItem(IShellItem shellItem);
        void SetProperties(IntPtr propertyStore);
        void SetCollectedProperties(IntPtr propertyDescriptionList, [MarshalAs(UnmanagedType.Bool)] bool appendDefault);
        void GetProperties(out IntPtr propertyStore);
        void ApplyProperties(IShellItem shellItem, IntPtr propertyStore, IntPtr window, IntPtr progressSink);
    }

    [ComImport]
    [Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        void BindToHandler(IntPtr bindingContext, in Guid handlerId, in Guid interfaceId, out IntPtr result);
        void GetPropertyStore(int flags, in Guid interfaceId, out IntPtr propertyStore);
        void GetPropertyDescriptionList(in IntPtr propertyKey, in Guid interfaceId, out IntPtr propertyDescriptionList);
        void GetAttributes(ShellItemArrayAttributeFlags attributes, uint mask, out uint arrayAttributes);
        void GetCount(out uint count);
        void GetItemAt(uint index, out IShellItem shellItem);
        void EnumItems(out IntPtr enumShellItems);
    }

    [Flags]
    private enum ShellItemArrayAttributeFlags : uint
    {
        None = 0
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindingContext, in Guid handlerId, in Guid interfaceId, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(ShellItemDisplayName displayName, [MarshalAs(UnmanagedType.LPWStr)] out string name);
        void GetAttributes(uint attributes, out uint itemAttributes);
        void Compare(IShellItem shellItem, uint hint, out int order);
    }
}
