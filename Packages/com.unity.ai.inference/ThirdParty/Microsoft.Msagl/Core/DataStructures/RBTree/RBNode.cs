namespace Microsoft.Msagl.Core.DataStructures {


    // Not [Serializable]: Unity can't serialize self-referencing nodes (UAC1005) and nothing serializes this tree.
    internal class RBNode<T> {

        public RBColor color;
        public T Item;
        public RBNode<T> parent, left, right;
        public RBNode(RBColor color) { this.color = color; }
        public RBNode(RBColor color, T item, RBNode<T> p, RBNode<T> left, RBNode<T> right) {
            this.color = color;
            this.parent = p;
            this.left = left;
            this.right = right;
            this.Item = item;
        }

        /// <summary>
        /// </summary>
        /// <returns></returns>
        public override string ToString() {
            return Item.ToString();
        }
    }
}
