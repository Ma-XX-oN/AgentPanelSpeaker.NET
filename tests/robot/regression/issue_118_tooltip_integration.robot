*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    regression    issue-118    permanent    ui    tooltip

*** Test Cases ***
MainForm Uses Central AppToolTip
    File Should Contain Text V1
    ...    ${EXECDIR}${/}AgentPanelSpeaker${/}MainForm.cs
    ...    private readonly AppToolTip _toolTip = new();

UiText Preserves AppToolTip Dispatch
    File Should Contain Text V1
    ...    ${EXECDIR}${/}AgentPanelSpeaker${/}UiText.cs
    ...    AppToolTip? toolTip = null

Bluetooth Utility Button Has Tooltip
    [Template]    Ui Text Should Equal V1
    ${TEST_PROBE}    Main.BluetoothWake.Tooltip    Bluetooth audio wake settings

Save Utility Button Has Tooltip
    [Template]    Ui Text Should Equal V1
    ${TEST_PROBE}    Main.SaveSettings.Tooltip    Save settings

Reset Utility Button Uses Concise Tooltip
    [Template]    Ui Text Should Equal V1
    ${TEST_PROBE}    Main.ResetDefaults.Tooltip    Reset settings

Hotkeys Utility Button Has Tooltip
    [Template]    Ui Text Should Equal V1
    ${TEST_PROBE}    Main.Hotkeys.Tooltip    Configure hotkeys

Application Tooltip Leaves Pointer Clearance
    [Template]    Static Method Should Return V1
    ${TEST_PROBE}
    ...    AgentPanelSpeaker.AppToolTip
    ...    GetPlacementContractSnapshot
    ...    []
    ...    {"presentationGapPixels":32,"minimumPointerClearancePixels":32}
