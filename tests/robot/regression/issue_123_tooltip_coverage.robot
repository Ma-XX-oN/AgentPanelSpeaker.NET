*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    regression    issue-123    permanent    ui    tooltip

*** Test Cases ***
Global Tooltip Alias Uses AppToolTip
    [Template]    File Should Contain Text V1
    ${EXECDIR}${/}AgentPanelSpeaker${/}GlobalUsings.cs    global using ToolTip = AgentPanelSpeaker.AppToolTip;

AppToolTip Central Registration Accounting Works
    [Template]    Static Method Should Return V1
    ${TEST_PROBE}
    ...    AgentPanelSpeaker.AppToolTip
    ...    GetCentralizationContractSnapshot
    ...    []
    ...    {"initiallyUnregistered":true,"firstRegistrationVisible":true,"secondRegistrationSurvivesFirstRemoval":true,"finalRemovalClearsRegistration":true}

Generic Coverage Does Not Manufacture Tooltips From Labels Or Values
    [Template]    Static Method Should Return V1
    ${TEST_PROBE}
    ...    AgentPanelSpeaker.TooltipCoverage
    ...    GetContractSnapshot
    ...    []
    ...    {"labelledSliderCovered":false,"textButtonCovered":false,"curatedTooltipPreserved":true,"passiveLabelExcluded":true,"explicitHoverPopupTooltipSuppressed":true,"unparentedTranscriptSettingsTooltipSuppressed":true}

Poll Interval Has Purpose Tooltip Text
    [Template]    Ui Text Should Equal V1
    ${TEST_PROBE}
    ...    Main.PollInterval.Description
    ...    Set how often AgentPanelSpeaker checks the selected session for new content.

Pronunciations Button Has Purpose Tooltip Text
    [Template]    Ui Text Should Equal V1
    ${TEST_PROBE}
    ...    Main.Pronunciations.Description
    ...    Open spelling and pronunciation rules, including IPA pronunciations.

Bluetooth Wake Button Has Purpose Tooltip Text
    [Template]    Ui Text Should Equal V1
    ${TEST_PROBE}
    ...    Main.BluetoothWake.Tooltip
    ...    Configure the wake audio used to keep Bluetooth devices ready for speech.

Application Idle Audits Every Open Form
    [Template]    File Should Contain Text V1
    ${EXECDIR}${/}AgentPanelSpeaker${/}TooltipCoverage.cs    Application.Idle += ApplicationIdle;

Hover Popup Anchors Are Explicitly Exempt
    [Template]    File Should Contain Text V1
    ${EXECDIR}${/}AgentPanelSpeaker${/}TooltipCoverage.cs    IsHoverPopupExempt

UiText Reuses Accessibility Description For Tooltip
    [Template]    File Should Contain Text V1
    ${EXECDIR}${/}AgentPanelSpeaker${/}UiText.cs    GetOptional($"{resourcePrefix}.Tooltip") ?? control.AccessibleDescription

No Raw WinForms Tooltip Bypass Exists
    ${result}=    Run Process    git    grep    -n    System.Windows.Forms.ToolTip    --    AgentPanelSpeaker    stdout=PIPE    stderr=PIPE
    Should Be Equal As Integers    ${result.rc}    0
    ${output}=    Evaluate    $result.stdout.strip()
    Should Match Regexp    ${output}    ^AgentPanelSpeaker/AppToolTip\\.cs:\\d+:internal sealed class AppToolTip : System\\.Windows\\.Forms\\.ToolTip$
