[Setup]
AppId={{F481CCBC-9EF8-47E5-8977-8DECA7FD4883}
AppName=PhoneMic
AppVersion=0.3
DefaultDirName={autopf}\PhoneMic
DefaultGroupName=PhoneMic
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
PrivilegesRequired=admin
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=yes
CloseApplications=no
RestartApplications=no
SetupIconFile=..\windows\PhoneMic.ico
UninstallDisplayIcon={app}\PhoneMic.exe
LicenseFile=documentation\LICENSE.txt
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
OutputDir=..\release\windows
OutputBaseFilename=PhoneMicSetup

[Messages]
WelcomeLabel1=Welcome to PhoneMic Setup
WelcomeLabel2=PhoneMic turns your Android smartphone into a wireless microphone for your Windows computer.%n%nClick Next to continue.
FinishedLabel=PhoneMic has been installed successfully.

[Tasks]
Name: "startmenu"; Description: "Create a Start Menu shortcut"
Name: "desktop"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "startup"; Description: "Start PhoneMic with Windows"; Flags: unchecked

[Files]
Source: "..\.phonemic-tools\packaging\PhoneMic.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "documentation\*.txt"; DestDir: "{app}\Documentation"; Flags: ignoreversion

[Icons]
Name: "{group}\PhoneMic"; Filename: "{app}\PhoneMic.exe"; Tasks: startmenu
Name: "{commondesktop}\PhoneMic"; Filename: "{app}\PhoneMic.exe"; Tasks: desktop

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "PhoneMic"; ValueData: """{app}\PhoneMic.exe"" --tray"; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "PhoneMic"; Tasks: not startup; Flags: deletevalue

[Run]
Filename: "{app}\PhoneMic.exe"; Description: "Launch PhoneMic now"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: LaunchAfterSetup

[Code]
var
  AcceptTerms, DriverRestart: TNewCheckBox;
  CablePage: TWizardPage;
  CableStatus: TNewStaticText;
  CableReady: Boolean;

procedure AgreeClick(Sender: TObject);
begin
  WizardForm.LicenseAcceptedRadio.Checked := AcceptTerms.Checked;
  WizardForm.LicenseNotAcceptedRadio.Checked := not AcceptTerms.Checked;
  if WizardForm.CurPageID = wpLicense then
    WizardForm.NextButton.Enabled := AcceptTerms.Checked;
end;

procedure BrowseVendor(Sender: TObject);
var
  Code: Integer;
begin
  if not ShellExecAsOriginalUser('open', 'https://vb-audio.com/Cable/', '', '',
      SW_SHOWNORMAL, ewNoWait, Code) then
    MsgBox('Open https://vb-audio.com/Cable/ to download VB-CABLE.', mbInformation, MB_OK);
end;

procedure CheckCable(Sender: TObject);
var
  Code: Integer;
begin
  ExtractTemporaryFile('PhoneMic.exe');
  CableReady := Exec(ExpandConstant('{tmp}\PhoneMic.exe'), '--check-cable', '',
    SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
  if CableReady then
    CableStatus.Caption := 'VB-CABLE is available. Setup will preserve it.'
  else
    CableStatus.Caption := 'VB-CABLE is unavailable. Download the standard VB-CABLE package, extract all files, and run VBCABLE_Setup_x64.exe as administrator. Follow the vendor''s restart instructions, then check again.';
  Log('VB-CABLE endpoint check: ' + CableStatus.Caption);
end;

procedure InitializeWizard;
var
  Intro: TNewStaticText;
  Download, Retry: TNewButton;
begin
  WizardForm.LicenseAcceptedRadio.Visible := False;
  WizardForm.LicenseNotAcceptedRadio.Visible := False;
  AcceptTerms := TNewCheckBox.Create(WizardForm);
  AcceptTerms.Parent := WizardForm.LicensePage;
  AcceptTerms.SetBounds(WizardForm.LicenseAcceptedRadio.Left,
    WizardForm.LicenseAcceptedRadio.Top, WizardForm.LicenseMemo.Width, ScaleY(19));
  AcceptTerms.Caption := 'I have read and agree to the PhoneMic Terms and Conditions.';
  AcceptTerms.OnClick := @AgreeClick;

  CablePage := CreateCustomPage(wpSelectTasks, 'Virtual Microphone Driver Setup',
    'Check whether the Windows virtual microphone is available.');
  Intro := TNewStaticText.Create(CablePage);
  Intro.Parent := CablePage.Surface;
  Intro.SetBounds(0, 0, CablePage.SurfaceWidth, ScaleY(58));
  Intro.AutoSize := False;
  Intro.WordWrap := True;
  Intro.Caption := 'PhoneMic uses VB-Audio Virtual Cable to make your Android phone''s microphone available to Windows applications. VB-CABLE is donationware from VB-Audio. Donations and license purchases are welcome.';
  CableStatus := TNewStaticText.Create(CablePage);
  CableStatus.Parent := CablePage.Surface;
  CableStatus.SetBounds(0, ScaleY(67), CablePage.SurfaceWidth, ScaleY(70));
  CableStatus.AutoSize := False;
  CableStatus.WordWrap := True;
  Download := TNewButton.Create(CablePage);
  Download.Parent := CablePage.Surface;
  Download.SetBounds(0, ScaleY(145), ScaleX(165), ScaleY(24));
  Download.Caption := 'Open VB-Audio download page';
  Download.OnClick := @BrowseVendor;
  Retry := TNewButton.Create(CablePage);
  Retry.Parent := CablePage.Surface;
  Retry.SetBounds(ScaleX(177), ScaleY(145), ScaleX(96), ScaleY(24));
  Retry.Caption := 'Check again';
  Retry.OnClick := @CheckCable;
  DriverRestart := TNewCheckBox.Create(CablePage);
  DriverRestart.Parent := CablePage.Surface;
  DriverRestart.SetBounds(0, ScaleY(183), CablePage.SurfaceWidth, ScaleY(34));
  DriverRestart.Caption := 'I installed VB-CABLE and still need to restart Windows.';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpLicense then AgreeClick(nil);
  if CurPageID = CablePage.ID then CheckCable(nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpLicense) and not WizardSilent then
    Result := AcceptTerms.Checked;
  if (CurPageID = CablePage.ID) and not CableReady and not DriverRestart.Checked and not WizardSilent then
    Result := MsgBox('Install PhoneMic now without a working virtual microphone? You can set up VB-CABLE later.',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

function NeedRestart: Boolean;
begin
  Result := DriverRestart.Checked and not WizardSilent;
end;

function LaunchAfterSetup: Boolean;
begin
  Result := not NeedRestart;
end;

function OpenEventW(Access: LongWord; Inherit: Boolean; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function OpenMutexW(Access: LongWord; Inherit: Boolean; Name: String): THandle;
  external 'OpenMutexW@kernel32.dll stdcall';
function SetEvent(Handle: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function ReleaseMutex(Handle: THandle): Boolean;
  external 'ReleaseMutex@kernel32.dll stdcall';

function StopInstalledReceiver: Boolean;
var
  SignalHandle, MutexHandle: THandle;
  WaitResult: LongWord;
begin
  Result := True;
  if not FileExists(ExpandConstant('{app}\PhoneMic.exe')) then Exit;
  MutexHandle := OpenMutexW($00100001, False, 'Local\PhoneMicReceiver');
  if MutexHandle = 0 then Exit;
  try
    SignalHandle := OpenEventW($0002, False, 'Local\PhoneMicReceiver.Stop');
    if SignalHandle <> 0 then begin
      SetEvent(SignalHandle);
      CloseHandle(SignalHandle);
    end;
    WaitResult := WaitForSingleObject(MutexHandle, 5000);
    Result := (WaitResult = 0) or (WaitResult = $80);
    if Result then ReleaseMutex(MutexHandle);
  finally
    CloseHandle(MutexHandle);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not StopInstalledReceiver then
    Result := 'Exit PhoneMic from its notification area icon, then run Setup again.';
end;

function InitializeUninstall: Boolean;
begin
  Result := StopInstalledReceiver;
  if not Result then
    SuppressibleMsgBox('Exit PhoneMic from its notification area icon, then run the uninstaller again.',
      mbError, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir, StartupValue: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'PhoneMic', StartupValue) and
    (CompareText(StartupValue, ExpandConstant('"{app}\PhoneMic.exe" --tray')) = 0) then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'PhoneMic');
  DataDir := ExpandConstant('{localappdata}\PhoneMic');
  if FileExists(DataDir + '\receiver.txt') and not UninstallSilent and
    (MsgBox('Remove the saved PhoneMic receiver settings for this Windows account?',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then begin
    if not DeleteFile(DataDir + '\receiver.txt') then
      MsgBox('Windows could not remove the saved PhoneMic settings.', mbError, MB_OK);
    RemoveDir(DataDir);
  end;
end;
