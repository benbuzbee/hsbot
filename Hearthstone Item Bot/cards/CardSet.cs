using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSBot.Cards
{
    /// <summary>
    /// A set of cards
    /// </summary>
    public class CardSet : IEnumerable<Card>
    {
        /// <summary>
        /// List of the cards in the set
        /// </summary>
        private List<Card> _cards = new List<Card>();

        public CardSet(params Card[] cards)
        {
            _cards.AddRange(cards);
        }

        public IEnumerator<Card> GetEnumerator()
        {
            return _cards.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        /// <summary>
        /// Gets a card at a specific index in the CardSet, or null if out of range
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public Card this[int index]
        {
            get 
            {
                lock (_cards)
                {
                    if (index >= 0 && index < _cards.Count)
                    {
                        return _cards[index];
                    }
                }
                return null;

            }
            
        }

        public int Count
        {
            get
            {
                return _cards.Count;
            }
            
        }

        /// <summary>
        /// Inserts a card into the card set at the given position. A negative or out of range number means append to end
        /// </summary>
        /// <param name="c"></param>
        /// <param name="position"></param>
        public void Insert(Card c, int position = -1)
        {
            lock (_cards)
            {
                if (position < 0 || position >= _cards.Count)
                    _cards.Add(c);
                else
                    _cards.Insert(position, c);
            }
        }

    }
}
