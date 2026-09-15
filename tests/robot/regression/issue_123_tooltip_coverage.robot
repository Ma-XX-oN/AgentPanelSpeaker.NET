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

Tooltip Coverage Covers Controls And Suppresses Hover Popup Tooltips
    [Template]    Static Method Should Return V1
    ${TEST_PROBE}
    ...    AgentPanelSpeaker.TooltipCoverage
    ...    GetContractSnapshot
    ...    []
    ...    {"labelledSliderCovered":true,"textButtonCovered":true,"curatedTooltipPreserved":true,"passiveLabelExcluded":true,"explicitHoverPopupTooltipSuppressed":true,"unparentedTranscriptSettingsTooltipSuppressed":true}

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
