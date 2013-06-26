
/**
* @ngdoc directive 
* @name umbraco.directive:valTab
* @restrict A
* @description Used to show validation warnings for a tab to indicate that the tab content has validations errors in its data.
**/
function valTab() {
    return {
        require: "^form",
        restrict: "A",
        link: function (scope, element, attr, formCtrl) {
            
            var tabId = "tab" + scope.tab.id;
            
            scope.formCtrl = formCtrl;
            scope.tabHasError = false;

            //watch the current form's validation for the current field name
            scope.$watch("formCtrl.$valid", function (isValid, lastValue) {
                if (isValid === undefined) {
                    return;
                }
                
                var tabContent = element.closest(".umb-panel").find("#" + tabId);

                if (!isValid) {
                    //check if the validation messages are contained inside of this tabs 
                    if (tabContent.find(".ng-invalid").length > 0) {
                        scope.tabHasError = true;
                    }
                    else {
                        scope.tabHasError = false;
                    }
                }
                else {
                    scope.tabHasError = false;
                }
            });
        }
    };
}
angular.module('umbraco.directives').directive("valTab", valTab);