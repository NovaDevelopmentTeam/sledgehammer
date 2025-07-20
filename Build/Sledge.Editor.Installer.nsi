; Sledge NSIS Installer
; ---------------------
!define PRODUCT_NAME "Sledge Editor"
!define PRODUCT_VERSION "{version}"

Name "${PRODUCT_NAME}"
OutFile "${PRODUCT_NAME}.${PRODUCT_VERSION}.exe"
InstallDir "$PROGRAMFILES\${PRODUCT_NAME}"
InstallDirRegKey HKLM "Software\Sledge\Editor" "InstallDir"
RequestExecutionLevel admin

; Version Info
VIProductVersion "${PRODUCT_VERSION}"
VIAddVersionKey "FileVersion" "${PRODUCT_VERSION}"
VIAddVersionKey "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey "FileDescription" "Installer for ${PRODUCT_NAME}"
VIAddVersionKey "LegalCopyright" "http://logic-and-trick.com 2018"

!include LogicLib.nsh

Function .onInit
    UserInfo::GetAccountType
    pop $0
    ${If} $0 != "admin"
        MessageBox mb_iconstop "Administrator rights required!" /SD IDOK
        SetErrorLevel 740
        Quit
    ${EndIf}
FunctionEnd

; Installer Pages
Page components
Page directory
Page instfiles

UninstPage uninstConfirm
UninstPage instfiles

; Installer Sections

Section "${PRODUCT_NAME}"
    IfSilent 0 +2
        Sleep 2000

    SectionIn RO
    SetOutPath $INSTDIR

    ; Purge junk from old installs
    Delete "$INSTDIR\*.dll"
    Delete "$INSTDIR\*.pdb"
    Delete "$INSTDIR\Sledge.Editor.Elevate.exe"
    Delete "$INSTDIR\Sledge.Editor.Updater.exe"
    Delete "$INSTDIR\UpdateSources.txt"

    File /r "Build\*"
    
    WriteRegStr HKLM "Software\Sledge\Editor" "InstallDir" "$INSTDIR"
    WriteRegStr HKLM "Software\Sledge\Editor" "Version" "${PRODUCT_VERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\SledgeEditor" "DisplayName" "${PRODUCT_NAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\SledgeEditor" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\SledgeEditor" "NoModify" 1
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\SledgeEditor" "NoRepair" 1
    WriteUninstaller "Uninstall.exe"
SectionEnd

Section "Start Menu Shortcuts"
    IfSilent 0 +2
        Goto end

    SetShellVarContext all
    CreateDirectory "$SMPROGRAMS\${PRODUCT_NAME}"
    CreateShortCut "$SMPROGRAMS\${PRODUCT_NAME}\Uninstall.lnk" "$INSTDIR\Uninstall.exe" "" "$INSTDIR\Uninstall.exe" 0
    CreateShortCut "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk" "$INSTDIR\Sledge.Editor.exe" "" "$INSTDIR\Sledge.Editor.exe" 0

    end:
SectionEnd

Section "Desktop Shortcut"
    IfSilent 0 +2
        Goto end

    SetShellVarContext all
    CreateShortCut "$DESKTOP\${PRODUCT_NAME}.lnk" "$INSTDIR\Sledge.Editor.exe" "" "$INSTDIR\Sledge.Editor.exe" 0

    end:
SectionEnd

Section "Run ${PRODUCT_NAME} After Installation"
    SetAutoClose true
    Exec "$INSTDIR\Sledge.Editor.exe"
SectionEnd

; Uninstall Section

Section "Uninstall"

  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\SledgeEditor"
  DeleteRegKey HKLM "Software\Sledge\Editor"

  SetShellVarContext all
  Delete "$SMPROGRAMS\${PRODUCT_NAME}\*.*"
  Delete "$DESKTOP\${PRODUCT_NAME}.lnk"

  RMDir /r "$SMPROGRAMS\${PRODUCT_NAME}"
  RMDir /r "$INSTDIR"

SectionEnd

; Optional: Uncomment for language support
; !include "MUI2.nsh"
; !insertmacro MUI_LANGUAGE "English"
