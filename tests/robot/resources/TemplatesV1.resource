*** Settings ***
Library    OperatingSystem
Library    Process

*** Variables ***
${TEMPLATE_API_VERSION}    1

*** Keywords ***
Command Should Succeed V1
    [Documentation]    Run a checked-in command contract and require exit code zero.
    [Arguments]    ${program}    @{arguments}
    TRY
        ${result}=    Run Process    ${program}    @{arguments}    stdout=PIPE    stderr=STDOUT
    EXCEPT    AS    ${error}
        Fail    INFRASTRUCTURE ERROR: ${error}
    END
    IF    ${result.rc} != 0
        Fail    Expected '${program}' to exit 0 but got ${result.rc}. Output: ${result.stdout}
    END

Command Should Fail For Reason V1
    [Documentation]    Require a nonzero exit and a specific diagnostic fragment.
    [Arguments]    ${program}    ${required_text}    @{arguments}
    TRY
        ${result}=    Run Process    ${program}    @{arguments}    stdout=PIPE    stderr=STDOUT
    EXCEPT    AS    ${error}
        Fail    INFRASTRUCTURE ERROR: ${error}
    END
    IF    ${result.rc} == 0
        Fail    Expected '${program}' to fail, but it exited 0.
    END
    Should Contain    ${result.stdout}    ${required_text}
    ...    msg=Command failed for the wrong reason. Required '${required_text}'. Output: ${result.stdout}

Path Should Exist V1
    [Documentation]    Require a file to exist.
    [Arguments]    ${path}
    OperatingSystem.File Should Exist    ${path}

File Should Contain Text V1
    [Documentation]    Require a UTF-8/text file to contain an exact fragment.
    [Arguments]    ${path}    ${required_text}
    OperatingSystem.File Should Exist    ${path}
    ${content}=    Get File    ${path}
    Should Contain    ${content}    ${required_text}

Text File Should Match Regex V1
    [Documentation]    Require a text file to contain a regular-expression match.
    [Arguments]    ${path}    ${pattern}
    OperatingSystem.File Should Exist    ${path}
    ${content}=    Get File    ${path}
    Should Match Regexp    ${content}    ${pattern}

Relative Difference Should Be At Most V1
    [Documentation]    Compare two numeric values using abs(actual-expected)/max(abs(expected),1e-12).
    [Arguments]    ${actual}    ${expected}    ${maximum_relative_difference}
    ${actual_number}=    Convert To Number    ${actual}
    ${expected_number}=    Convert To Number    ${expected}
    ${limit}=    Convert To Number    ${maximum_relative_difference}
    ${difference}=    Evaluate    abs($actual_number - $expected_number) / max(abs($expected_number), 1e-12)
    IF    ${difference} > ${limit}
        Fail    Relative difference ${difference} exceeds maximum ${limit}; actual=${actual_number}, expected=${expected_number}.
    END

Probe Should Return Json V1
    [Documentation]    Run the generic application test probe and compare its canonical JSON output.
    [Arguments]    ${probe}    ${command}    ${expected_json}    @{arguments}
    TRY
        ${result}=    Run Process    ${probe}    ${command}    @{arguments}    stdout=PIPE    stderr=PIPE
    EXCEPT    AS    ${error}
        Fail    INFRASTRUCTURE ERROR: ${error}
    END
    IF    ${result.rc} != 0
        Fail    Probe '${command}' failed with ${result.rc}. Error: ${result.stderr}
    END
    ${actual}=    Evaluate    $result.stdout.strip()
    Should Be Equal As Strings    ${actual}    ${expected_json}

Static Method Should Return V1
    [Documentation]    Invoke one static application method through the generic probe.
    [Arguments]    ${probe}    ${type}    ${method}    ${arguments_json}    ${expected_json}
    Probe Should Return Json V1
    ...    ${probe}
    ...    static-method
    ...    ${expected_json}
    ...    ${type}
    ...    ${method}
    ...    ${arguments_json}

Setting Contract Should Match V1
    [Documentation]    Verify default, serialization round trip, change tracking, and selective merge for one setting.
    [Arguments]    ${probe}    ${property}    ${alternate_json}    ${change_key}    ${expected_json}
    Probe Should Return Json V1
    ...    ${probe}
    ...    setting-contract
    ...    ${expected_json}
    ...    ${property}
    ...    ${alternate_json}
    ...    ${change_key}

Ui Text Should Equal V1
    [Documentation]    Resolve a production UI resource and compare its decoded text.
    [Arguments]    ${probe}    ${resource_key}    ${expected_text}
    ${result}=    Run Process    ${probe}    ui-text    ${resource_key}    stdout=PIPE    stderr=PIPE
    IF    ${result.rc} != 0
        Fail    UI text probe failed with ${result.rc}. Error: ${result.stderr}
    END
    ${text}=    Evaluate    json.loads($result.stdout)    modules=json
    Should Be Equal As Strings    ${text}    ${expected_text}

Ui Text Should Contain V1
    [Documentation]    Resolve a production UI resource and require a text fragment.
    [Arguments]    ${probe}    ${resource_key}    ${required_text}
    ${result}=    Run Process    ${probe}    ui-text    ${resource_key}    stdout=PIPE    stderr=PIPE
    IF    ${result.rc} != 0
        Fail    UI text probe failed with ${result.rc}. Error: ${result.stderr}
    END
    ${text}=    Evaluate    json.loads($result.stdout)    modules=json
    Should Contain    ${text}    ${required_text}
