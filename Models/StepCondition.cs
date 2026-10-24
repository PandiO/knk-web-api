namespace knkwebapi_v2.Models
{
    /// <summary>
    /// Represents a condition that must be met before a step can be started or completed.
    /// Used for advanced wizard flows where steps have prerequisites.
    /// 
    /// CONDITION TYPES:
    /// 1. START CONDITION: Determines if the user can navigate to this step.
    ///    Example: "User Agreement" step requires "Age Verification" step to be completed.
    /// 2. COMPLETION CONDITION: Determines if the user can proceed to the next step.
    ///    Example: "Payment" step requires "Total Amount > 0" before allowing "Next".
    /// 
    /// USE CASES:
    /// - Skip optional steps based on previous inputs (e.g., skip "Shipping Address" for digital products).
    /// - Enforce prerequisites (e.g., must complete "Basic Info" before accessing "Advanced Settings").
    /// - Block progression until external validation passes (e.g., "Email Verification" step).
    /// </summary>
    public class StepCondition
    {
        public int Id { get; set; }
        
        /// <summary>
        /// Type of condition: "StartCondition" or "CompletionCondition".
        /// - StartCondition: Evaluated when user tries to navigate TO this step.
        /// - CompletionCondition: Evaluated when user clicks "Next" or "Finish" on this step.
        /// </summary>
        public string ConditionType { get; set; } = null!;
        
        /// <summary>
        /// JSON object storing the condition logic.
        /// Similar to DependencyConditionJson in FormField, but operates at the step level.
        /// 
        /// EXAMPLES:
        /// Simple field check: { "field": "AgreedToTerms", "operator": "equals", "value": true }
        /// Previous step check: { "stepIndex": 0, "completed": true }
        /// Multiple conditions: { "operator": "and", "conditions": [...] }
        /// External validation: { "type": "api", "endpoint": "/api/validate/email-verified", "field": "Email" }
        /// 
        /// Frontend evaluates this before allowing navigation or progression.
        /// Backend can also enforce these conditions when receiving form submission.
        /// </summary>
        public string ConditionLogicJson { get; set; } = "{}";
        
        /// <summary>
        /// Error message displayed when the condition is not met.
        /// Example: "You must agree to the terms and conditions before proceeding."
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// False switches the condition off without deleting it; the wizard then skips it. Until
        /// 2026-10-23 (KNG-186) this existed on the DTO only and the API always sent false, so no
        /// completion condition ever ran. Existing rows are active.
        /// </summary>
        public bool IsActive { get; set; } = true;
        
        // Foreign key to FormStep
        public int FormStepId { get; set; }
        public FormStep FormStep { get; set; } = null!;
    }
}
