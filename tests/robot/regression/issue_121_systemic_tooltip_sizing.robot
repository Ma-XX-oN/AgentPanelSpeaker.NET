*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    regression    issue-121    permanent    ui    tooltip

*** Test Cases ***
Tooltip Sizing Contract Preserves Complete Captions
    [Template]    Static Method Should Return V1
    ${TEST_PROBE}
    ...    AgentPanelSpeaker.AppToolTip
    ...    GetSizingContractSnapshot
    ...    []
    ...    {"shortCaptionFits":true,"explicitTwoLineCaptionFits":true,"longCaptionWrapsWithinWorkingArea":true,"longCaptionUsesMoreHeightWhenConstrained":true,"paddingIsIncluded":true,"rightEdgePlacementClamped":true,"bottomEdgePlacementFlipsAbove":true}

App Tooltip Registers Central Popup Sizing Handler
    [Template]    File Should Contain Text V1
    ${EXECDIR}${/}AgentPanelSpeaker${/}AppToolTip.cs    Popup += ToolTipPopup;

Tooltip Measurement Uses Central Text Format Contract
    [Template]    File Should Contain Text V1
    ${EXECDIR}${/}AgentPanelSpeaker${/}AppToolTip.cs    ToolTipTextFormat);
