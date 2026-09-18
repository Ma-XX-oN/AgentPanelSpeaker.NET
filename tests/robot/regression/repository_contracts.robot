*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    regression    infrastructure    permanent

*** Test Cases ***
Development Version Guard Has Issue Shape
    [Template]    Text File Should Match Regex V1
    ${EXECDIR}${/}DEVELOPMENT-VERSION    ^[0-9]+[.][0-9]+[.][0-9]+-issue[.][0-9]+[.][0-9]+$

Project Has Authoritative Version Property
    [Template]    Text File Should Match Regex V1
    ${EXECDIR}${/}AgentPanelSpeaker${/}AgentPanelSpeaker.csproj    <Version>[0-9]+[.][0-9]+[.][0-9]+(?:-issue[.][0-9]+[.][0-9]+)?</Version>

AIConversationCore Pin Authorities Agree
    [Template]    Command Should Succeed V1
    pwsh    -NoProfile    -File    ${EXECDIR}${/}tools${/}Verify-CorePinConsistency.ps1

Test Suite Completion Guard Self Test Passes
    [Template]    Command Should Succeed V1
    pwsh    -NoProfile    -File    ${EXECDIR}${/}tools${/}Invoke-TestSuite.ps1    -SelfTest

Unknown Repository Task Is Rejected
    [Template]    Command Should Fail For Reason V1
    pwsh    Unknown repository task 'intentionally-missing-task-108'    -NoProfile    -File    ${EXECDIR}${/}tools${/}Invoke-RepositoryTask.ps1    -Task    intentionally-missing-task-108

Development Version Tool Advances A New Issue To Iteration One
    ${fixture}=    Set Variable    ${OUTPUT DIR}${/}development-version-fixture
    Create Directory    ${fixture}
    ${guard}=    Set Variable    ${fixture}${/}DEVELOPMENT-VERSION
    ${project}=    Set Variable    ${fixture}${/}AgentPanelSpeaker.csproj
    Create File    ${guard}    1.2.3-issue.94.3
    Create File    ${project}    <Project><PropertyGroup><Version>1.2.3-issue.94.3</Version></PropertyGroup></Project>
    Command Should Succeed V1
    ...    pwsh
    ...    -NoProfile
    ...    -File
    ...    ${EXECDIR}${/}tools${/}Set-DevelopmentVersion.ps1
    ...    -Issue
    ...    108
    ...    -DevelopmentVersionPath
    ...    ${guard}
    ...    -ProjectPath
    ...    ${project}
    File Should Contain Text V1    ${guard}    1.2.3-issue.108.1
    File Should Contain Text V1    ${project}    <Version>1.2.3-issue.108.1</Version>
    [Teardown]    Remove Directory    ${fixture}    recursive=True
