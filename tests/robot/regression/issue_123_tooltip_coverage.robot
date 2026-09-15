*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    regression    issue-123    permanent    ui    tooltip

*** Test Cases ***
Global Tooltip Alias Uses AppToolTip
    [Template]    File Should Contain Text V1
    ${EXECDIR}${/}AgentPanelSpeaker${/}GlobalUsings.cs    global using ToolTip = AgentPanelSpeaker.AppToolTip;

AppToolTip Exposes Central Registration Audit
    [Template]    File Should Contain Text V1
    ${EXECDIR}${/}AgentPanelSpeaker${/}AppToolTip.cs    HasCentralToolTip

Tooltip Coverage Policy Exists
    [Template]    Path Should Exist V1
    ${EXECDIR}${/}AgentPanelSpeaker${/}TooltipCoverage.cs

Theme Pass Schedules Tooltip Coverage
    [Template]    File Should Contain Text V1
    ${EXECDIR}${/}AgentPanelSpeaker${/}ThemeManager.cs    TooltipCoverage.Schedule(control, dark);

Hover Popup Anchors Are Explicitly Exempt
    [Template]    File Should Contain Text V1
    ${EXECDIR}${/}AgentPanelSpeaker${/}TooltipCoverage.cs    IsHoverPopupExempt

UiText Reuses Accessibility Description For Tooltip
    [Template]    File Should Contain Text V1
    ${EXECDIR}${/}AgentPanelSpeaker${/}UiText.cs    GetOptional($"{resourcePrefix}.Tooltip") ?? control.AccessibleDescription
