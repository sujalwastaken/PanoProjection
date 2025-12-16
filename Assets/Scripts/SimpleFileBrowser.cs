using System;
using System.Runtime.InteropServices;
using System.IO; // Required for Directory.GetCurrentDirectory
using UnityEngine;

public class SimpleFileBrowser
{
    // --- SAVE FILE WRAPPER ---
    public static string SaveFile(string title, string defaultName, string extension)
    {
        // 1. CACHE CURRENT DIRECTORY
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
            ofn.initialDir = UnityEngine.Application.dataPath;
            ofn.title = title;
            ofn.defExt = extension;
            ofn.flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000002 | 0x00000008; // OFN_NOCHANGEDIR (0x00000008) is supposed to help, but we force restore anyway

            ofn.file = defaultName;

            if (GetSaveFileName(ofn)) return ofn.file;
            return null;
        }
        finally
        {
            // 2. RESTORE DIRECTORY (No matter what happens)
            Directory.SetCurrentDirectory(originalPath);
        }
    }

    // --- OPEN FILE WRAPPER ---
    public static string OpenFile(string title)
    {
        // 1. CACHE CURRENT DIRECTORY
        string originalPath = Directory.GetCurrentDirectory();

        try
        {
            OpenFileName ofn = new OpenFileName();
            ofn.structSize = Marshal.SizeOf(ofn);
            ofn.filter = "Image Files\0*.png;*.jpg;*.jpeg\0All Files\0*.*\0";
            ofn.file = new string(new char[256]);
            ofn.maxFile = ofn.file.Length;
            ofn.fileTitle = new string(new char[64]);
            ofn.maxFileTitle = ofn.fileTitle.Length;
            ofn.initialDir = UnityEngine.Application.dataPath;
            ofn.title = title;
            ofn.flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008; // Added OFN_NOCHANGEDIR

            if (GetOpenFileName(ofn)) return ofn.file;
            return null;
        }
        finally
        {
            // 2. RESTORE DIRECTORY
            Directory.SetCurrentDirectory(originalPath);
        }
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