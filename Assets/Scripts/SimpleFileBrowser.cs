using System;
using System.Runtime.InteropServices;
using System.IO;
using UnityEngine;

public class SimpleFileBrowser
{
    // --- SAVE FILE WRAPPER ---
    public static string SaveFile(string title, string defaultName, string extension)
    {
        string originalPath = Directory.GetCurrentDirectory();
        try
        {
            OpenFileName ofn = new OpenFileName();
            ofn.structSize = Marshal.SizeOf(ofn);
            ofn.filter = $"All Files\0*.*\0{extension.ToUpper()} Files\0*.{extension}\0";
            ofn.file = new string(new char[256]);
            ofn.maxFile = ofn.file.Length;
            ofn.fileTitle = new string(new char[64]);
            ofn.maxFileTitle = ofn.fileTitle.Length;

#if UNITY_EDITOR
            ofn.initialDir = UnityEngine.Application.dataPath;
#else
            ofn.initialDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
#endif
            ofn.title = title;
            ofn.defExt = extension;
            ofn.flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000002 | 0x00000008;
            ofn.file = defaultName;

            if (GetSaveFileName(ofn)) return ofn.file;
            return null;
        }
        finally { Directory.SetCurrentDirectory(originalPath); }
    }

    // --- OPEN FILE (LEGACY OVERLOAD) ---
    // Fixes CS7036 in PanoramaInteraction.cs
    // Defaults to Image Files if no extension is provided
    public static string OpenFile(string title)
    {
        return OpenFileWithFilter(title, "Image Files\0*.png;*.jpg;*.jpeg\0All Files\0*.*\0");
    }

    // --- OPEN FILE (NEW OVERLOAD) ---
    // Fixes PanoramaProjectIO.cs (allows specific extension like .panon)
    public static string OpenFile(string title, string extension)
    {
        string filter = $"{extension.ToUpper()} Files\0*.{extension}\0All Files\0*.*\0";
        return OpenFileWithFilter(title, filter);
    }

    // --- INTERNAL HELPER ---
    private static string OpenFileWithFilter(string title, string filter)
    {
        string originalPath = Directory.GetCurrentDirectory();
        try
        {
            OpenFileName ofn = new OpenFileName();
            ofn.structSize = Marshal.SizeOf(ofn);
            ofn.filter = filter;
            ofn.file = new string(new char[256]);
            ofn.maxFile = ofn.file.Length;
            ofn.fileTitle = new string(new char[64]);
            ofn.maxFileTitle = ofn.fileTitle.Length;

#if UNITY_EDITOR
            ofn.initialDir = UnityEngine.Application.dataPath;
#else
            ofn.initialDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
#endif
            ofn.title = title;
            ofn.flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008;

            if (GetOpenFileName(ofn)) return ofn.file;
            return null;
        }
        finally { Directory.SetCurrentDirectory(originalPath); }
    }

    // --- WINDOWS API ---
    [DllImport("Comdlg32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetSaveFileName([In, Out] OpenFileName ofn);

    [DllImport("Comdlg32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetOpenFileName([In, Out] OpenFileName ofn);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class OpenFileName
    {
        public int structSize = 0;
        public IntPtr dlgOwner = IntPtr.Zero;
        public IntPtr instance = IntPtr.Zero;
        public String filter = null;
        public String customFilter = null;
        public int maxCustFilter = 0;
        public int filterIndex = 0;
        public String file = null;
        public int maxFile = 0;
        public String fileTitle = null;
        public int maxFileTitle = 0;
        public String initialDir = null;
        public String title = null;
        public int flags = 0;
        public short fileOffset = 0;
        public short fileExtension = 0;
        public String defExt = null;
        public IntPtr custData = IntPtr.Zero;
        public IntPtr hook = IntPtr.Zero;
        public String templateName = null;
        public IntPtr reservedPtr = IntPtr.Zero;
        public int reservedInt = 0;
        public int flagsEx = 0;
    }
}