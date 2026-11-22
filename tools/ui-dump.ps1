param(
    [string]$TitlePattern = "九序",
    [switch]$GroupByColumn
)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName WindowsBase

$root = [System.Windows.Automation.AutomationElement]::RootElement
$condition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, $TitlePattern)
$window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)

if (-not $window) {
    # 退一步：模糊匹配
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($item in $all) {
        if ($item.Current.Name -like "*$TitlePattern*") { $window = $item; break }
    }
}

if (-not $window) {
    Write-Output "WINDOW_NOT_FOUND"
    exit 1
}

Write-Output "WINDOW: '$($window.Current.Name)' rect=$($window.Current.BoundingRectangle)"

$descendants = $window.FindAll(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)

$rows = foreach ($element in $descendants) {
    $rect = $element.Current.BoundingRectangle
    [PSCustomObject]@{
        Type = $element.Current.ControlType.ProgrammaticName -replace 'ControlType\.', ''
        Name = $element.Current.Name
        X    = [Math]::Round($rect.X)
        Y    = [Math]::Round($rect.Y)
        W    = [Math]::Round($rect.Width)
        H    = [Math]::Round($rect.Height)
        Id   = $element.Current.AutomationId
    }
}

$rows | Sort-Object Y, X | Format-Table -AutoSize | Out-String -Width 200
