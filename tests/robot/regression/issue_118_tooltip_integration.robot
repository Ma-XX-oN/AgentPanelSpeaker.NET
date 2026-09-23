*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    regression    issue-118    permanent    ui    tooltip

*** Test Cases ***
Central Tooltip Alias Targets AppToolTip
    File Should Contain Text V1
    ...    ${EXECDIR}${/}AgentPanelSpeaker${/}GlobalUsings.cs
    ...    global using ToolTip = AgentPanelSpeaker.AppToolTip;

MainForm Uses Central Tooltip Alias
    File Should Contain Text V1
    ...    ${EXECDIR}${/}AgentPanelSpeaker${/}MainForm.cs
    ...    private readonly ToolTip _toolTip = new();

UiText Uses Central Tooltip Alias
    File Should Contain Text V1
    ...    ${EXECDIR}${/}AgentPanelSpeaker${/}UiText.cs
    ...    ToolTip? toolTip = null

Bluetooth Utility Button Explains Its Action
    [Template]    Ui Text Should Equal V1
    ${TEST_PROBE}    Main.BluetoothWake.Tooltip    Configure the wake audio used to keep Bluetooth devices ready for speech.

Save Utility Button Explains Its Action
    [Template]    Ui Text Should Equal V1
    ${TEST_PROBE}    Main.SaveSettings.Tooltip    Choose which changed settings to save.

Reset Utility Button Explains Its Action
    [Template]    Ui Text Should Equal V1
    ${TEST_PROBE}    Main.ResetDefaults.Tooltip    Choose which settings to reset to their default values.

Hotkeys Utility Button Explains Its Action
    [Template]    Ui Text Should Equal V1
    ${TEST_PROBE}    Main.Hotkeys.Tooltip    Open the keyboard shortcut settings.

Disabled Registered Control Uses Parent Pointer Fallback
    [Template]    Static Method Should Return V1
    ${TEST_PROBE}
    ...    AgentPanelSpeaker.AppToolTip
    ...    GetDisabledControlContractSnapshot
    ...    []
    ...    {"disabledControlPointerFallback":true,"disabledPointerSchedulesPresentation":true,"leavingDisabledControlCancelsPresentation":true}

Application Tooltip Leaves Pointer Clearance
    [Template]    Static Method Should Return V1
    ${TEST_PROBE}
    ...    AgentPanelSpeaker.AppToolTip
    ...    GetPlacementContractSnapshot
    ...    []
    ...    {"presentationGapPixels":32,"minimumPointerClearancePixels":32}
