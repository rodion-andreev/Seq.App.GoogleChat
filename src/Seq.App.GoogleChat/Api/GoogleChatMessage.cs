using System.Collections.Generic;

namespace Seq.App.GoogleChat.Api
{
    public class GoogleChatMessage(Card card)
    {
        public CardV2[] CardsV2 { get; set; } = [new CardV2(card)];
    }

    public class CardV2(Card card)
    {
        public Card Card { get; set; } = card;
    }

    public class Card
    {
        public Header Header { get; set; }

        public List<Section> Sections { get; set; }
    }

    public class Header
    {
        public string Title { get; set; }

        public string Subtitle { get; set; }
    }

    public class Section
    {
        public string Header { get; set; }

        public bool Collapsible = true;

        public int UncollapsibleWidgetsCount = 1;

        public List<Widget> Widgets { get; set; }
    }

    public class Widget
    {
        public TextParagraph TextParagraph { get; set; }

        public Widget(string text)
        {
            TextParagraph = new TextParagraph { Text = text };
        }

        public Widget(string text, int maxLines)
        {
            TextParagraph = new TextParagraph { Text = text, MaxLines = maxLines };
        }
    }

    public class TextParagraph
    {
        public string Text { get; set; }
        public int MaxLines { get; set; } = 2;
    }
}