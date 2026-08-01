Unicode true
RequestExecutionLevel user
SetCompressor /SOLID lzma

!ifndef PUBLISH_DIR
!error "PUBLISH_DIR must point to the published FlowCast files."
!endif

!ifndef OUTPUT_DIR
!error "OUTPUT_DIR must point to the release output directory."
!endif

!ifndef PRODUCT_VERSION
!define PRODUCT_VERSION "1.0.0"
!endif

Name "FlowCast"
OutFile "${OUTPUT_DIR}\FlowCast-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\FlowCast"
InstallDirRegKey HKCU "Software\FlowCast" "InstallPath"

Page directory
Page instfiles
UninstPage uninstConfirm
UninstPage instfiles

Function .onInit
    IfFileExists "$INSTDIR\AudioShare.App.exe" 0 done
    ClearErrors
    Rename "$INSTDIR\AudioShare.App.exe" "$INSTDIR\.flowcast-install-lockcheck.exe"
    IfErrors 0 unlocked
    MessageBox MB_ICONEXCLAMATION|MB_OK "Please exit FlowCast, then run Setup again."
    Abort
unlocked:
    Rename "$INSTDIR\.flowcast-install-lockcheck.exe" "$INSTDIR\AudioShare.App.exe"
done:
FunctionEnd

Section "Install FlowCast"
    RMDir /r "$INSTDIR"
    SetOutPath "$INSTDIR"
    File /r "${PUBLISH_DIR}\*.*"

    WriteRegStr HKCU "Software\FlowCast" "InstallPath" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FlowCast" "DisplayName" "FlowCast"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FlowCast" "DisplayVersion" "${PRODUCT_VERSION}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FlowCast" "Publisher" "FlowCast"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FlowCast" "UninstallString" "$\"$INSTDIR\Uninstall FlowCast.exe$\""
    WriteUninstaller "$INSTDIR\Uninstall FlowCast.exe"

    StrCmp $INSTDIR "$LOCALAPPDATA\Programs\FlowCast" 0 skipShortcuts
    CreateDirectory "$SMPROGRAMS\FlowCast"
    CreateShortcut "$SMPROGRAMS\FlowCast\FlowCast.lnk" "$INSTDIR\AudioShare.App.exe"
    CreateShortcut "$DESKTOP\FlowCast.lnk" "$INSTDIR\AudioShare.App.exe"
skipShortcuts:
SectionEnd

Section "Uninstall"
    Delete "$DESKTOP\FlowCast.lnk"
    Delete "$SMPROGRAMS\FlowCast\FlowCast.lnk"
    RMDir "$SMPROGRAMS\FlowCast"
    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FlowCast"
    DeleteRegKey HKCU "Software\FlowCast"
    RMDir /r "$INSTDIR"
SectionEnd
