using System;
using System.Collections.Generic;

namespace OnlyTweakWindows
{
    internal sealed class SessionSafety
    {
        private readonly HashSet<string> uncertain=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string Key(OperationRequest request) {
            if (request.ActionId.StartsWith("ad.",StringComparison.Ordinal))
                return "ad|"+request.Target+"|"+request.Parameters.GetValueOrDefault("objectGuid","");
            string operation=request.ActionId=="endpoint.verify" ? request.Parameters.GetValueOrDefault("operation","") : request.ActionId;
            return operation+"|"+request.Target;
        }
        public void Authorize(OperationRequest request)
        {
            if (EnterpriseCatalog.Get(request.ActionId).IsChange && uncertain.Contains(Key(request)))
                throw new InvalidOperationException("Esito precedente non verificato: rileggi prima lo stato del medesimo oggetto/intervento.");
        }
        public void Observe(OperationRequest request,StepResult result)
        {
            if (EnterpriseCatalog.Get(request.ActionId).IsChange && result.State==StepState.Unverified)
                uncertain.Add(Key(request));
            if ((request.ActionId=="ad.userstate" || request.ActionId=="endpoint.verify") &&
                result.State==StepState.Success && result.AuthenticationVerified) uncertain.Remove(Key(request));
        }
    }
}
