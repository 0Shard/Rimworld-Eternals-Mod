// Relative Path: Eternal/Source/Eternal/Interfaces/IFoodDebtSystem.cs
// Creation Date: 03-12-2025
// Last Edit: 29-12-2025
// Author: 0Shard
// Description: Combined interface for food debt management system. Inherits from IFoodDebtReader
//              and IFoodDebtWriter for interface segregation. Clients needing only read or write
//              access can use the more specific interfaces.

namespace Eternal.Interfaces
{
    /// <summary>
    /// Combined interface for food debt management system.
    /// Provides unified API for tracking, adding, and repaying food debt for Eternal pawns.
    /// </summary>
    /// <remarks>
    /// Key behaviors:
    /// - Both living and dead pawns can accumulate debt during healing/regrowth
    /// - Debt increases hunger rate (via StatPart_EternalHungerRate)
    /// - Food consumption splits between debt repayment and hunger (via Pawn_Ingestion_Patch)
    /// - Higher debt = faster hunger = more eating = faster repayment (self-balancing)
    ///
    /// Interface Segregation:
    /// - Use IFoodDebtReader for read-only access (UI, queries)
    /// - Use IFoodDebtWriter for mutation access (healing systems)
    /// - Use IFoodDebtSystem for full access (main component)
    /// </remarks>
    public interface IFoodDebtSystem : IFoodDebtReader, IFoodDebtWriter
    {
        // All members inherited from IFoodDebtReader and IFoodDebtWriter
    }
}
