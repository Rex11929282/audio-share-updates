Unicode true
RequestExecutionLevel user
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "nsDialogs.nsh"

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

Var CreateDesktopShortcut
Var DesktopShortcutCheckbox

Page directory
Page custom DesktopShortcutPage DesktopShortcutPageLeave
Page instfiles
UninstPage uninstConfirm
UninstPage instfiles

Function .onInit
    StrCpy $CreateDesktopShortcut "1"
    nsExec::ExecToStack '"$SYSDIR\tasklist.exe" /FI "IMAGENAME eq AudioShare.App.exe" /FO CSV /NH'
    Pop $0
    Pop $1
    StrCpy $2 $1 1
    StrCmp $2 "$\"" 0 done
    IfSilent silentLocked interactiveLocked
interactiveLocked:
    MessageBox MB_ICONEXCLAMATION|MB_OK "Please exit FlowCast, then run Setup again."
silentLocked:
    SetErrorLevel 1
    Quit
done:
FunctionEnd

Function DesktopShortcutPage
    nsDialogs::Create 1018
    Pop $0
    ${NSD_CreateLabel} 0 0 100% 24u "要在桌面建立 FlowCast 快捷方式吗？"
    Pop $0
    ${NSD_CreateCheckbox} 0 30u 100% 12u "在桌面建立 FlowCast 快捷方式"
    Pop $DesktopShortcutCheckbox
    ${NSD_Check} $DesktopShortcutCheckbox
    nsDialogs::Show
FunctionEnd

Function DesktopShortcutPageLeave
    ${NSD_GetState} $DesktopShortcutCheckbox $CreateDesktopShortcut
FunctionEnd

Section "Install FlowCast"
    RMDir /r "$INSTDIR"
    SetOutPath "$INSTDIR"
    File /r "${PUBLISH_DIR}\*.*"
    SetFileAttributes "$INSTDIR\router-helper" HIDDEN

    WriteRegStr HKCU "Software\FlowCast" "InstallPath" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FlowCast" "DisplayName" "FlowCast"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FlowCast" "DisplayVersion" "${PRODUCT_VERSION}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FlowCast" "Publisher" "FlowCast"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FlowCast" "UninstallString" "$\"$INSTDIR\Uninstall FlowCast.exe$\""
    WriteUninstaller "$INSTDIR\Uninstall FlowCast.exe"

    StrCmp $INSTDIR "$LOCALAPPDATA\Programs\FlowCast" 0 skipShortcuts
    CreateDirectory "$SMPROGRAMS\FlowCast"
    CreateShortcut "$SMPROGRAMS\FlowCast\FlowCast.lnk" "$INSTDIR\AudioShare.App.exe"
    StrCmp $CreateDesktopShortcut 1 0 skipDesktopShortcut
    CreateShortcut "$DESKTOP\FlowCast.lnk" "$INSTDIR\AudioShare.App.exe"
skipDesktopShortcut:
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
