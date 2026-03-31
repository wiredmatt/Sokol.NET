namespace TexasHoldem.Logic.Players
{
#if WEB
    using System.Threading.Tasks;
#endif

    public interface IPlayer
    {
        string Name { get; }

        int BuyIn { get; }

        void StartGame(IStartGameContext context);

        void StartHand(IStartHandContext context);

        void StartRound(IStartRoundContext context);

        PlayerAction PostingBlind(IPostingBlindContext context);

        PlayerAction GetTurn(IGetTurnContext context);

#if WEB
        Task<PlayerAction> GetTurnAsync(IGetTurnContext context);
#endif

        void EndRound(IEndRoundContext context);

        void EndHand(IEndHandContext context);

        void EndGame(IEndGameContext context);
    }
}
